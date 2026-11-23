# PhoneBridge build script
# Publishes the app, code-signs it, compiles the Inno Setup installer, signs it.
#
# Usage:
#   .\build.ps1                          # unsigned build
#   .\build.ps1 -PfxPath cert.pfx        # signs, prompts for PFX password securely
#   $env:CODESIGN_PASSWORD='...' ; .\build.ps1 -PfxPath cert.pfx   # non-interactive
#   .\build.ps1 -TestCert                # self-signed test cert (LOCAL TESTING ONLY)
#
# Real distribution builds must use a certificate from a public CA
# (Sectigo, DigiCert, Certum, ...). Self-signed certs trigger SmartScreen warnings.

param(
    [string]$PfxPath = $env:CODESIGN_PFX,
    [string]$CertSubject = $env:CODESIGN_SUBJECT,   # cert already in Windows store (cloud/token certs, e.g. Certum SimplySign)
    [switch]$TestCert,
    [string]$TimestampUrl = "http://timestamp.sectigo.com",
    [string]$TestCertSubject = "PhoneBridge Test Signing"
)

$ErrorActionPreference = "Stop"
$root    = Split-Path -Parent $PSScriptRoot   # allows running from installer\
$publish = Join-Path $root "publish"
$dist    = Join-Path $root "dist"
$signtool = Join-Path $root "vendor\signtool\signtool.exe"
$iscc    = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"

# signtool ships in the Microsoft.Windows.SDK.BuildTools NuGet package;
# download it automatically on a fresh clone
if (-not (Test-Path $signtool)) {
    Write-Host "signtool not found - fetching from nuget.org..." -ForegroundColor Yellow
    $pkg = Join-Path $root "vendor\sdktools.nupkg"
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools" -OutFile $pkg
    # nupkg is a zip: extract the x64 signtool from the newest version folder
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($pkg)
    $entry = $zip.Entries | Where-Object { $_.FullName -match '^bin/[\d.]+/x64/signtool.exe$' } |
        Sort-Object { $_.FullName } -Descending | Select-Object -First 1
    New-Item (Split-Path $signtool) -ItemType Directory -Force | Out-Null
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $signtool, $true)
    $zip.Dispose()
    Remove-Item $pkg
    Write-Host "  signtool ready."
}

if (-not (Test-Path $signtool)) { throw "signtool not found at $signtool" }
if (-not (Test-Path $iscc))     { throw "ISCC.exe not found at $iscc - install Inno Setup 6 (winget install JRSoftware.InnoSetup)" }

# ---------- locate dotnet ----------
$dotnet = "dotnet"
if (Test-Path "D:\dev\dotnet\dotnet.exe") { $dotnet = "D:\dev\dotnet\dotnet.exe" }

# ---------- read version ----------
$csproj = Get-ChildItem (Join-Path $root "src") -Recurse -Filter *.csproj | Select-Object -First 1
$version = ([xml](Get-Content $csproj.FullName)).Project.PropertyGroup.Version
Write-Host "== PhoneBridge v$version ==" -ForegroundColor Cyan

# ---------- signing helpers ----------
function Get-SignArgs {
    param([string]$File)

    if ($TestCert) {
        $cert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Subject -match [regex]::Escape($TestCertSubject) -and $_.HasPrivateKey } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if (-not $cert) {
            Write-Host "Creating self-signed test certificate..." -ForegroundColor Yellow
            $cert = New-SelfSignedCertificate -Subject "CN=$TestCertSubject" `
                -Type CodeSigningCert -KeyUsage DigitalSignature `
                -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
                -NotAfter (Get-Date).AddYears(3) -CertStoreLocation Cert:\CurrentUser\My
            # trust chain locally so signatures verify on this machine
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root","CurrentUser")
            $store.Open("ReadWrite")
            $store.Add($cert)
            $store.Close()
        }
        return @("sign","/fd","SHA256","/td","SHA256","/tr",$TimestampUrl,"/sha1",$cert.Thumbprint,"/q")
    }
    elseif ($CertSubject) {
        # certificate already installed in the Windows store (hardware token / cloud signing)
        # resolve to thumbprint: signtool's /n matching is unreliable with token providers
        $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.HasPrivateKey -and $_.Subject -like "*$CertSubject*" } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if (-not $cert) { throw "Certificate matching '$CertSubject' with private key not found in store. Is your token software (e.g. SimplySign) running?" }
        Write-Host "  using store cert: $($cert.Subject) ($($cert.Thumbprint))"
        return @("sign","/fd","SHA256","/td","SHA256","/tr",$TimestampUrl,"/sha1",$cert.Thumbprint,"/q")
    }
    elseif ($PfxPath -and (Test-Path $PfxPath)) {
        $pw = $env:CODESIGN_PASSWORD
        $secure = $null
        if (-not $pw) {
            $secure = Read-Host "Password for $PfxPath" -AsSecureString
            $pw = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        }
        return @("sign","/fd","SHA256","/td","SHA256","/tr",$TimestampUrl,
                 "/f",$PfxPath,"/p",$pw,"/q")
    }
    return $null
}

function Invoke-Sign {
    param([string]$File)
    $args2 = Get-SignArgs -File $File
    if (-not $args2) { Write-Host "  (skipped signing, no certificate configured) $File" -ForegroundColor DarkYellow; return }
    Write-Host "  signing: $(Split-Path -Leaf $File)"
    & $signtool @args2 $File
    if ($LASTEXITCODE -ne 0) { throw "signtool failed on $File (exit $LASTEXITCODE)" }
}

function Show-Signature {
    param([string]$File)
    $sig = Get-AuthenticodeSignature $File
    $name = Split-Path -Leaf $File
    Write-Host ("  {0,-32} {1}" -f $name, $sig.Status) -ForegroundColor $(if ($sig.Status -eq 'Valid') {'Green'} else {'Yellow'})
}

# ---------- 1. publish ----------
Write-Host "[1/4] Publishing (win-x64, self-contained)..." -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& $dotnet publish $csproj.FullName -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# ---------- 2. sign app exe ----------
Write-Host "[2/4] Code-signing application..." -ForegroundColor Cyan
Invoke-Sign -File (Join-Path $publish "PhoneBridge.exe")

# ---------- 4. sign installer ----------
Write-Host "[3/4] Compiling installer..." -ForegroundColor Cyan
& $iscc (Join-Path $root "installer\PhoneBridge.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
$setup = Get-ChildItem $dist -Filter "PhoneBridge_Setup_*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host "[4/4] Code-signing installer..." -ForegroundColor Cyan
Invoke-Sign -File $setup.FullName

# ---------- 5. hard gate: verify signatures ----------
Write-Host "[5/5] Verifying signatures..." -ForegroundColor Cyan
$signingEnabled = ($TestCert -or $CertSubject -or ($PfxPath -and (Test-Path $PfxPath)))
if (-not $signingEnabled) {
    Write-Host "  WARNING: no certificate configured - build is UNSIGNED." -ForegroundColor Red
    Write-Host "  Set CODESIGN_PFX or use -TestCert. Do not distribute this build." -ForegroundColor Red
}
else {
    foreach ($f in @((Join-Path $publish "PhoneBridge.exe"), $setup.FullName)) {
        # exit-code check (quiet, fast)
        & $signtool verify /pa /q $f | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "SIGNATURE VERIFICATION FAILED: $f - refusing to ship." }
        # status check (catches trust issues signtool /q may not surface)
        $sig = Get-AuthenticodeSignature $f
        if ($sig.Status -ne 'Valid') {
            throw "SIGNATURE STATUS '$($sig.Status)' on $f : $($sig.StatusMessage) - refusing to ship."
        }
        # timestamp check: a missing timestamp breaks long-term validity
        if (-not $sig.TimeStamperCertificate) {
            throw "NO TIMESTAMP on $f - refusing to ship."
        }
        Write-Host "  OK: $(Split-Path -Leaf $f) (signed + timestamped + trusted)"
    }
}

Write-Host ""
Write-Host "Build output:" -ForegroundColor Cyan
Show-Signature -File (Join-Path $publish "PhoneBridge.exe")
Show-Signature -File $setup.FullName
Write-Host ("  installer: {0}  ({1:N0} MB)" -f $setup.FullName, ($setup.Length/1MB))
