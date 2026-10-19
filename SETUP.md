# Lantoss setup

This repository contains the complete Lantoss 1.6 source. The Windows executable
is generated from the source; it is not checked into Git.

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
its disk-space requirements. See VERIFICATION.md for the checks performed on 1.6.

## Publish a standalone Windows app

```powershell
.\source\build.ps1 -Mode Publish
.\app\Lantoss.exe
```

`Publish` creates `app\Lantoss.exe` for Windows x64 with the .NET runtime included.
The receiving PC does not need the SDK. If distributing the executable, include
the root third-party license and notice files with it. This is an unsigned
development build. The generated `app` folder is ignored by Git.

## Connect your devices

1. Close any older FlowShare/Lantoss instance, then open Lantoss on both PCs.
2. Connect them to the same trusted home Wi-Fi or Ethernet network. Nearby PCs
   are discovered and connected automatically by default.
3. Approve Windows administrator access if asked for the app's scoped Private-
   network firewall setup. The helper does not change your network category.
   If your trusted home network is currently Public, use Windows network settings
   to classify it appropriately. Do not enable this setup on an untrusted network.
4. Choose files and send. Manual transfers require the recipient's approval.
5. To use a phone, open **Phone & browser**, scan the QR code and use the same
   home Wi-Fi. **Open in browser** opens the local transfer page on this PC.
6. Create a **Shared Space** to expose a selected folder. On the other PC, browse
   it inside the app. For automatic two-way sync, link it to a local folder once
   and approve the host's ongoing sync permission. Both apps must remain open.

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
