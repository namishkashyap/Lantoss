# Lantoss setup

This repository contains the complete Lantoss 1.9 source. The Windows executable
is generated from the source; it is not checked into Git.

## Install the built package

For normal use, double-click Lantoss-Setup-1.9.0-x64.msi and choose optional
Explorer, desktop and sign-in features in Custom Setup. No SDK is required to
run the installed application. See README.md for right-click sharing and tray
startup. Installation is per-user; firewall setup can separately request Windows
administrator approval for Private-network access.

## Prerequisites

- A Windows x64 PC and PowerShell.
- Git, plus access to this GitHub repository. Downloading the repository
  ZIP is also sufficient if you do not use Git.
- The .NET 10 SDK. A runtime-only installation cannot build the app.
- Node.js 20 or later for the optional browser UI regression checks.
- Internet access for the initial NuGet restore and package vulnerability checks.

The source pins the .NET 10 SDK family in `global.json` and allows newer stable
feature bands within that family. The build script keeps SDK caches and build
intermediates under the ignored `work` directory. No API key, cloud account or
database is needed to run file sharing.

## Clone and build

```powershell
git clone https://github.com/namishkashyap/Lantoss.git
Set-Location Lantoss
dotnet --version
.\source\build.ps1 -Mode Build
```

All commands below run from the repository root. To use a specific SDK or a
different intermediate directory:

```powershell
.\source\build.ps1 -Mode Build -DotnetPath 'C:\path\to\dotnet.exe' -BuildRoot 'C:\path\to\scratch'
```

If PowerShell blocks local scripts, use your organization's approved script
execution policy or unblock the downloaded source after reviewing it. The build
does not require changing the machine-wide execution policy.

## Run verification

```powershell
.\source\build.ps1 -Mode Test
.\source\build.ps1 -Mode CheckFormat
node --check .\source\Browser\portal.js
node .\source\tests\browser-ui.test.cjs
```

The C# suite uses scratch files and loopback connections with memory-only test
identities. Firewall tests mock the Windows firewall commands; they do not alter
real firewall rules. A symbolic-link fixture may skip when Windows denies link
creation. `TestLarge` is a separate optional large-file check; see README.md for
its disk-space requirements. See VERIFICATION.md for current and earlier results.
The 1.9 hostname tests use an isolated multicast group/port with loopback; they
do not claim names on the user's real Wi-Fi.

## Publish a standalone Windows app

```powershell
.\source\build.ps1 -Mode Publish
.\app\Lantoss.exe
```

`Publish` creates `app\Lantoss.exe` for Windows x64 with the .NET runtime included.
The receiving PC does not need the SDK. If distributing the executable, include
the root third-party license and notice files with it. This is an unsigned
development build. The generated `app` folder is ignored by Git.

## Build the Windows 11 integration and installer

Additional build-only tools: g++/windres from w64devkit x64 2.10.1 and the x64
MakeAppx/SignTool directory from Microsoft.Windows.SDK.BuildTools 10.0.26100.1742.
These can live under work; no global compiler installation is required.

- [Portable compiler](https://github.com/skeeto/w64devkit/releases/tag/v2.10.1),
  w64devkit-x64-2.10.1.7z.exe SHA256
  ADD1EDF984CE4D473CFEB7C52E0AB38DB73A855BBA6C7F564D5E16648EB2896A.
- [SDK build tools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.26100.1742),
  nupkg SHA256 DB432106A12E44FC1055F520B147DF183C86963AB79BAF81D8A4A9DD867E6606.
  Extract it as ZIP; use bin/10.0.26100.0/x64.

Create a private development signing key once, then reuse it. This command
creates files only; it does not trust the certificate on the build PC:

```powershell
$env:LANTOSS_PFX_PASSWORD = Read-Host 'PFX password'
.\source\Explorer\New-DevelopmentCertificate.ps1 -OutputPath .\work\signing-private.pfx
.\installer\build-installer.ps1 -CompilerPath 'C:\tools\w64devkit\bin\g++.exe' -SdkToolsPath 'C:\tools\sdk\bin\10.0.26100.0\x64' -SigningCertificatePath .\work\signing-private.pfx
```

The builder compiles/signs the native DLL and sparse package, embeds the matching
public certificate, publishes the app and builds the MSI. Keep the PFX private;
never include it in Git or a source ZIP. Source includes only Publisher.cer.
The PFX password is read only from LANTOSS_PFX_PASSWORD, never a parameter.
Signatures are SHA-256 with an RFC 3161 timestamp; pass -NoTimestamp for an
offline build. DotnetPath and BuildRoot parameters are supported. For a public
release, replace the development publisher subject in AppxManifest.xml,
RegisterPackage.ps1 and the build check, and use a trusted publisher certificate.

You can build/test Explorer separately with source/Explorer/build-explorer.ps1
using the same three tool/signing parameters and -RunTests. Set
LANTOSS_NATIVE_REQUEST to the printed fixture path before source/build.ps1 -Mode
Test to include native-to-managed multiselection verification. After building
Explorer, publish the app so it embeds the matching public certificate. When all
payloads already exist, installer/build-installer.ps1 -SkipPublish builds only MSI.
The installer builder downloads hash-checked WiX 4.0.6 tools into the build folder.

For target-desktop acceptance, follow [installer/TESTING.md](installer/TESTING.md).
The first Windows 11 installation asks for explicit development-publisher trust
and administrator approval. The main app remains per-user. Building and previewing
never install the MSI, change Windows trust or register the sparse package.

## Connect your devices

1. Close any older FlowShare/Lantoss instance, then open Lantoss on both PCs.
2. Connect them to the same trusted home Wi-Fi or Ethernet network. Nearby PCs
   are discovered and connected automatically by default.
3. Approve Windows administrator access if asked for the app's scoped Private-
   network firewall setup. The helper does not change your network category.
   If your trusted home network is currently Public, use Windows network settings
   to classify it appropriately. Do not enable this setup on an untrusted network.
4. Choose files and send. Manual transfers require the recipient's approval.
5. On a phone or PC on the same home Wi-Fi, open **http://share-wollvo.local**.
   Leave browser sharing and discovery on. The page lists other discovered 1.9
   PCs, each with its own automatic local name. **Phone & browser** also shows a
   QR code. If port 80 or .local resolution is unavailable, use **Show direct QR
   fallback**; no IP or port needs to be typed. See README.md for network limits.
6. Create a **Shared Space** to expose a selected folder. On the other PC, browse
   it inside the app. For automatic two-way sync, link it to a local folder once
   and approve the host's ongoing sync permission. Both apps must remain open.
7. Use **Text & links** to send a note to a connected PC or save it on This PC
   for its browser. The browser page has the matching board. Copy/open is explicit.

Shared-folder Refresh reloads the current subfolder without a full page reload.
Automatic sync uses notifications and cached hashes; idle checks avoid file
content reads. Metadata is swept every minute, with rolling checksum audits.
Use Sync now for immediate full checksum verification after an unusual edit.

Native transfers use TLS. The browser page uses local HTTP; optional access codes
gate access without encrypting that browser link. Sync preserves conflicts and
previous versions; deletions do not propagate. Read README.md before linking
valuable folders. Guest Wi-Fi isolation, VPN routing or organization policies can
still prevent direct communication even when the app's firewall rules are set.

## Appearance and existing installations

Choose **System**, **Light** or **Dark** near the top of the app sidebar. It saves
immediately. System follows Windows automatically. The phone page has its own
appearance selector and follows that device's system preference.

Fresh installations use `%LOCALAPPDATA%\Lantoss` and `Downloads\Lantoss Inbox`.
An existing FlowShare data store is reused in place so paired keys, folders,
history and sync permissions survive. Do not commit those runtime data files.

## Where to read next

- [README.md](README.md): usage, features, limits and file-sharing behavior.
- [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md): source map, architecture and invariants.
- [DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md): engineering and compatibility decisions.
- [VERIFICATION.md](VERIFICATION.md): completed local checks and device-test limits.
- [ROADMAP.md](ROADMAP.md): implemented and planned features.
