# PhoneBridge

A Windows desktop toolkit for Android phones and iPhones — the legitimate,
open counterpart to commercial "phone manager" suites. Built with .NET 8 / WPF.

## Features (v1.1.0)

- **Dashboard** — live detection of connected Android devices (via bundled ADB)
  and iPhones/iPads (via Apple's Mobile Device Service, requires iTunes or the
  "Apple Devices" app installed).
- **Apps** — list user-installed Android apps, uninstall for the current user,
  install APK files from the PC.
- **Files** — browse `/sdcard`, download files/folders to the PC, send files to
  the phone.
- **Tools** — Android screenshots (saved as PNG), reboot / recovery / bootloader,
  disk & battery info.
- **Backup** — one-click backup of user media (DCIM, Pictures, Download, Movies,
  Music, Documents, WhatsApp media) into a plain timestamped folder. No
  proprietary formats. Plus an **automatic daily backup** via a Windows
  scheduled task (headless `--backup <folder>` mode with a log file).
- **Export** — contacts, SMS history and call log to standard CSV files
  (opens in Excel).

Deliberately excluded: screen-lock removal, FRP/iCloud/activation-lock bypass,
or any other device-security circumvention.

## License & code signing

PhoneBridge is MIT-licensed. Because it is open source, it qualifies for
**free code-signing certificates**:

- [Certum Open Source Code Signing](https://www.certum.eu/) — free for FOSS developers
- [SignPath Foundation](https://signpath.org/) — free Authenticode signing for open-source projects

Until a CA certificate is configured, `build.ps1` falls back to a self-signed
test certificate (local testing only) or produces an unsigned build with a
prominent warning. Release binaries are only produced when the signature
verification gate passes.

## Layout

- `src/PhoneBridge/` — WPF application (C#)
  - `Services/AdbService.cs` — wraps bundled `adb.exe`
  - `Services/IosService.cs` — iPhone detection via WMI + Apple service check
  - `Services/ExportService.cs` — contacts/SMS/call-log → CSV
  - `Services/BackupRunner.cs` — shared media-backup routine (UI + scheduled task)
  - `Views/` — Dashboard, Apps, Files, Tools, Export, Backup pages
- `vendor/adb/` — Google platform-tools (adb + 2 DLLs), bundled at build time
- `installer/build.ps1` — one-shot build: publish → sign → installer → sign → verify
- `installer/PhoneBridge.iss` — Inno Setup script
- `installer/check-cert.ps1` — verifies a code-signing cert is visible in the Windows store

## Building from source

Run the one-shot build script (PowerShell):

```powershell
# unsigned build
cd installer; .\build.ps1

# signed with your code-signing certificate (PFX)
$env:CODESIGN_PFX = "C:\certs\mycert.pfx"
$env:CODESIGN_PASSWORD = "..."     # or omit to be prompted securely
.\build.ps1

# local testing with a self-signed cert (creates it + trusts it locally)
.\build.ps1 -TestCert
```

The script publishes (win-x64, self-contained), signs `PhoneBridge.exe`,
compiles the Inno Setup installer, signs the setup exe, and prints signature
status. Timestamping uses Sectigo (`http://timestamp.sectigo.com`) by default.

Manual steps, if you prefer:

1. Install the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`).
2. `dotnet build src/PhoneBridge/PhoneBridge.csproj -c Release`
3. `dotnet publish src/PhoneBridge/PhoneBridge.csproj -c Release -r win-x64
   --self-contained true -o publish`
4. Install Inno Setup 6 and compile `installer/PhoneBridge.iss`.

`vendor/signtool/signtool.exe` is Microsoft's official signtool, extracted
from the `Microsoft.Windows.SDK.BuildTools` NuGet package.

## Notes

- Android features need USB debugging enabled on the phone; the first
  connection shows an "allow this PC?" prompt on the device.
- iOS features are detection-level in v1; deeper iOS work would build on
  Apple's usbmuxd service.
- The installer is currently signed with a **self-signed test certificate**
  (`CN=PhoneBridge Test Signing`) so the whole pipeline is exercised locally.
  For public distribution, buy a code-signing certificate from a CA
  (Sectigo/DigiCert/Certum — OV is fine, ~$200–500/yr) and set `CODESIGN_PFX`.
  SmartScreen still needs some reputation history for new OV certs; EV certs
  get an immediate reputation boost.
- adb components: Google platform-tools, Apache License 2.0.
