# Checks that a code-signing certificate is visible in the Windows store
# and shows the exact subject string to use for CODESIGN_SUBJECT.
#
# Usage:  powershell -File check-cert.ps1
# Run while the SimplySign app (or your token software) is running.

$certs = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.HasPrivateKey -and
        ($_.EnhancedKeyUsageList | ForEach-Object { $_.FriendlyName }) -contains 'Code Signing'
    }

if (-not $certs) {
    Write-Host "No code-signing certificate found in the user store." -ForegroundColor Red
    Write-Host "Make sure your token software (e.g. Certum SimplySign) is running, then retry."
    exit 1
}

Write-Host "Code-signing certificate(s) found:`n" -ForegroundColor Green
foreach ($c in $certs) {
    Write-Host ("  Subject   : {0}" -f $c.Subject)
    Write-Host ("  Issuer    : {0}" -f $c.Issuer)
    Write-Host ("  Valid     : {0}  ->  {1}" -f $c.NotBefore.ToString("yyyy-MM-dd"), $c.NotAfter.ToString("yyyy-MM-dd"))
    Write-Host ("  Thumbprint: {0}" -f $c.Thumbprint)
    Write-Host ""
}

$best = $certs | Sort-Object NotAfter -Descending | Select-Object -First 1
Write-Host "Add this to your build environment (PowerShell):" -ForegroundColor Cyan
Write-Host ('  $env:CODESIGN_SUBJECT = "{0}"' -f $best.Subject) -ForegroundColor White
Write-Host ""
Write-Host "Then build with:"
Write-Host "  cd D:\dev\phonebridge\installer"
Write-Host "  .\build.ps1"
