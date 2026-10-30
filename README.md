# Lantoss 2.0 for Windows, macOS, Linux and phone browsers

Private home-Wi-Fi file sharing. Connections between computers are direct by
default: no password, typed code, or comparison prompt. Access-code protection
is optional.

## Versions and downloads

| Version | Platforms | Download | Source |
|---|---|---|---|
| **2.0.2** (current) | Windows (MSI) · macOS (Apple Silicon, Intel) · Linux (.deb, .tar.gz; x64, arm64) | [Release v2.0.2](https://github.com/namishkashyap/Lantoss/releases/tag/v2.0.2) | `main` |
| 2.0.0 | Same platforms | [Release v2.0.0](https://github.com/namishkashyap/Lantoss/releases/tag/v2.0.0) | tag `v2.0.0` |
| 1.9.0 (previous) | Windows (MSI) | [Release v1.9.0](https://github.com/namishkashyap/Lantoss/releases/tag/v1.9.0) | `release/1.x` |

Step-by-step install, first-run, firewall and uninstall instructions for each
platform are in **[INSTALL.md](INSTALL.md)**. All versions talk to each other
on the same network, and phones need no app.

## New in 2.0

- **macOS and Linux apps** built from the same sharing engine, with every
  screen of the Windows app: send, devices, transfers, phone & browser, text &
  links, shared spaces, two-way sync, history and settings.
- **People, not just devices.** Browsers connected through the web page appear
  in the device list with a name and picture. Each computer can set its own
  profile picture.
- **Incoming-file notifications** and sender details (name, device, address)
  in approvals, Transfers and History. An optional setting saves files in
  `Inbox/<sender>`.
- **Security fixes** and a bolder, cleaner design. See [CHANGELOG.txt](CHANGELOG.txt).

## Install on Windows

Download and double-click **Lantoss-Setup-2.0.2-x64.msi**. Close the older app
using **Exit Lantoss** in the Windows tray first. The installer installs for the
current Windows account under LocalAppData/Programs/Lantoss, creates Start menu
entries, and registers repair/uninstall in Windows Installed apps. The .NET
runtime is included. The Windows 11 identity package and native extension use a local development signing certificate; this is not a publicly trusted release. The MSI itself is unsigned.

## New in 1.9

### One local address

Open **http://share-wollvo.local** on a phone or PC connected to the same home
Wi-Fi. Keep Lantoss's sharing server, browser sharing and discovery enabled. The
app automatically listens on HTTP port 80 and advertises the local name through
mDNS; no IP address, port, router configuration or hosts-file edit is needed on a
network that supports local multicast name resolution.

If several PCs run Lantoss, exactly one claims the common name after checking
for conflicts. The page's **Other PCs** list opens each discovered PC's own local
name. When the common-name owner exits, another eligible PC takes over after its
goodbye message or the short DNS lease expires. Each PC also has a stable local
name derived from its saved device key. Update both PCs to 1.9 to advertise these
browser links. A DNS name identifies a destination; it does not authenticate it.

The short URL needs port 80 to be available and mDNS to be allowed by the device
and Wi-Fi. If port 80 is busy, Lantoss keeps browser sharing available on 48586
or an available port and explains the fallback in **Phone & browser**. Use
**Show direct QR fallback** on devices without working .local resolution; scanning
still avoids typing an IP or port. Guest Wi-Fi isolation, VPNs and managed
networks can block local communication. These cases cannot be fixed by claiming
a hostname. The app does not change router DNS, system DNS or hosts files.

### Text & links

Open **Text & links** in the sidebar. Choose a connected PC to send it a note or
URL, or choose **This PC** to make it available on this PC's phone/browser page.
The browser has a matching text-and-links area. Incoming notes appear there
without replacing your draft. Use **Copy**, **Open link** or native **Delete**
explicitly; text is never executed, opened or copied automatically. Only HTTP
and HTTPS links get an Open link action.

The board keeps the latest 100 notes within 2 MB, with up to 16,000 characters
per note. It is recent local sharing history, not a permanent notebook. Native
PC sends use the paired TLS connection; browser access follows the existing
optional-code/session policy and local HTTP transport. Share notes only on your
trusted Wi-Fi when code protection is off.

### Refresh and lighter sync

The shared-folder **Refresh** button reloads the folder you are currently viewing
in both the app and browser. Browser refresh preserves selected upload files and
ignores outdated responses if you navigate while a request is running.

Sync now uses filesystem notifications and cached manifests. Idle five-second
checks do not enumerate folders or read file contents. A metadata sweep every
60 seconds catches missed notifications; a rolling checksum audit checks one
old file per sweep after 30 minutes. **Sync now** performs a full checksum pass.
Hashes stream through one reader per scanner with a 64 KB buffer, caches are
bounded, and unchanged peer manifests omit their repeated file lists. Conflict
copies, previous versions, authorization checks and retry behavior are retained.
Large or active folders still need disk/CPU work; this is not block-level sync.

In Custom Setup, keep the application and Explorer sharing features. Optionally
select **Desktop shortcut** and **Launch Lantoss at sign-in** for installation.
With the feature-tree UI, select a feature's icon and choose to install it on
the local hard drive. Startup can also be enabled later in app Settings.

### Right-click sharing

Connect the other PC once in Lantoss. Right-click a file or folder in Explorer,
then **Share with Lantoss → the PC name** in Windows 11's main menu.
Choosing a named PC queues a normal encrypted
transfer; that recipient must still approve. **Choose a PC…** opens the selected
files in Send without sending until a recipient is chosen. Unpaired/blocked PCs
are omitted. Up to 32 remembered PCs are shown, with current labels refreshed
while Lantoss runs. The menu can launch the app if it is closed.

Windows 11 sends the entire selection in one bounded request (up to 256 items,
within a 96 KiB handoff limit). Existing unsent drafts are preserved for direct
named-PC sends. Windows 10 keeps its classic menu and 15-item selection limit.

During installation, the Windows 11 feature asks once to trust **Lantoss
Development** for app packages, then requests Windows administrator approval.
Only this build's public signing certificate goes into Local Machine Trusted
People; no root certificate is installed. Declining cancels this feature's install;
you can run setup again with Explorer sharing unchecked. No trust or package
registration is changed merely by building or previewing the app.

After upgrading, sign out and back in if Explorer still caches the old menu.
Settings can disable/re-enable the installed sharing menu. For a portable build,
install the MSI first to deploy the extension and its identity package.

### Simpler Home

Home now contains Choose files / Choose folder, a short device list and one
phone/browser entry. Transfer history, sync and advanced settings remain in the
sidebar. Light, Dark and System appearance continue to work.

### Startup and sharing server

In Settings, enable **Launch Lantoss in the tray when I sign in** and **Start
the sharing server automatically when Lantoss opens**, then save. The server
starts discovery, native receiving, the browser portal when enabled and linked
folder sync. **Start server** is also available in the top bar, Settings and
the tray. The Start menu includes **Lantoss in the tray** for a quiet launch.

With **Keep receiving and syncing in the tray when I close the window** enabled
(the default), closing the window hides it while sharing continues. Reopen via
the tray icon. Use **Exit Lantoss** to stop sharing and save history. It runs in
the signed-in user's session; it is not a Windows service and does not run before
sign-in or after sign-out. Windows Task Manager can independently disable startup.

Uninstall removes the executable, installed docs, shortcuts and Lantoss's own
current-user Explorer/startup entries. Received files, app metadata, sync folders
and retained versions are kept. Exit Lantoss before uninstalling or upgrading.
Uninstall also removes this user's Explorer identity package. The development
publisher certificate remains trusted for other installed Lantoss builds;
installer/TESTING.md explains how to remove it after all such builds are gone.

## Build from this repository

The complete source, tests, icon assets, third-party notices and developer
documentation are included. See [SETUP.md](SETUP.md) for prerequisites, cloning,
verification, publishing and connecting devices. The architecture and source map
are in [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md).

With the .NET 10 SDK on Windows, run from the repository root:

```powershell
.\source\build.ps1 -Mode Publish
.\app\Lantoss.exe
```

The standalone executable is generated locally. Build outputs and runtime
identity/settings files are excluded from Git.

## New in Lantoss 1.6

FlowShare is now **Lantoss**, including the executable, window, tray, browser
page, dialogs and exported history. A teal-and-mint palette replaces the blue
theme, with coordinated surfaces and readable action labels in both appearances.

Use **Appearance** near the top of the Windows sidebar: **System**, **Light** or
**Dark**. Changes save immediately and update the current page. System follows
the Windows app appearance automatically. The phone/browser header has its own
appearance selector; System follows that device, and the choice is saved in that
browser when local storage is available.

Existing FlowShare connection, folder, history and sync data is reused in place.
Close the older app before starting Lantoss. Fresh installations use
`%LOCALAPPDATA%\Lantoss` and `Downloads\Lantoss Inbox`. Upgraded installations
keep their current data and receive folders. The new executable path may need
one Windows administrator approval for Private-network firewall access.

The source includes comments around transfer integrity, folder permissions,
sync reconciliation, threading and compatibility. Start with **DEVELOPER_GUIDE.md**
for the architecture, build commands and a guide to the key source files.

## Start

Build the standalone app using the steps above, then run `app\Lantoss.exe` on
your Windows PC. If using a prebuilt distribution ZIP, extract it first. Close
any older copy before starting. A published executable includes the x64 runtime;
the SDK is needed to build from source, not to run the published app.

### PC to PC

1. Open this version on both PCs on the same trusted Wi-Fi/Ethernet network.
2. Discoverable PCs connect automatically by default and remember each other's
   public keys. Code-protected PCs still use **Connect** and their optional code.
3. Choose files/folders and **Send securely**. The recipient reviews the files
   and chooses **Accept files** or **Decline** before anything is saved. Approved
   sync links have a separate ongoing folder permission.

Native transfers use TLS and SHA-256 checks before atomic finalization. A direct
first connection trusts the selected device without a human code comparison;
the UI calls this a saved key, not a comparison-verified identity. Older clients
may still ask for comparison approval. Update both PCs for direct connection.

### Phone or any local browser

1. Click **Open in browser** in the Windows app's top bar to open its transfer
   page on this PC. Open **Phone & browser** for the phone QR/link.
2. Connect the phone to the same Wi-Fi. Scan the QR code with its camera and open
   the page, or enter the displayed address in its browser.
3. For PC-to-phone: choose **Share files with browser** or **Add a folder** in
   the PC app. The selected files appear as downloads on the phone page.
4. For phone-to-PC: choose files/photos on the phone page and press **Send to PC**.
   Approve the batch on the PC. The phone shows progress, measured speed,
   estimated time remaining, and completion.

No phone app is required. The browser can download selected files and browse
folders you explicitly register as Shared Spaces. It cannot browse other disk
locations. Individual file selections last for the current app session.
Removing a share keeps the original.
Folder selections expose their regular files individually, not a downloadable
folder archive. Phone uploads keep both copies of duplicate filenames.

The browser link uses **local HTTP, not encryption**. With code protection off,
people on the same reachable home network can open it and download the files
or spaces you selected. Use your trusted home Wi-Fi. A code controls access but does not
encrypt HTTP. Native PC-to-PC transfers remain encrypted independently.

### Shared Spaces

1. Open **Shared Spaces** and choose **Create space from a folder**.
2. New spaces are **Read only**. On the other PC, open **Shared Spaces**, select
   the host PC, and **Browse folder**. Browse subfolders and **Download** directly
   inside the Windows app. Downloads go to your configured inbox and keep both
   duplicate names. A browser is optional; phones still use the QR/link.
3. Choose **Read & upload** on the PC to allow browser uploads into that space
   or an existing subfolder. Use **Upload files here** in the Windows folder view,
   or choose a destination on the browser page. The host approves the batch; its
   destination stays in that space. Native browsing/uploads/downloads use TLS.
4. **Stop sharing** disables the space. **Remove space** unregisters it. Both
   stop its active transfers and keep its local files.

Registrations and permissions survive restarts. Spaces are available only while
this PC, the folder's drive, and Lantoss browser sharing are available. A
missing folder is shown unavailable, not recreated. Files uploaded into a space
become part of its shared contents; inbox uploads are not shared automatically.
Duplicate uploads keep both files. Browsing supports up to 1,000 entries in each
folder. Browser clients cannot delete, rename, or create folders. Permissions
apply to connected devices; per-person team roles are not included.

### Automatic two-way sync

1. Install the **same Lantoss version on both PCs** and keep both apps open. On the host, create a
   Shared Space and select **Read & upload**.
2. On just one of the PCs, open **Shared Spaces**, select the host, and choose
   **Set up automatic sync** for its space. Pick an existing local folder.
3. Review the folder link and approve **Allow automatic sync** once on the host.
   New and edited files now update in both directions without further prompts.
4. Open **Sync** for current status, **Sync now**, pause/resume, or remove link.
   The host can revoke an incoming sync permission there too.

Set up each folder pair once. Links, permissions, and prior checksums survive
restarts. The app checks changes about every five seconds while running, caches
unchanged-file hashes, and rehashes periodically. Offline or locked-file failures
retry with backoff up to a minute. A changed destination revision is reconciled
again instead of blindly overwritten. Missing root folders/drives are reported
unavailable. No cloud account or internet connection is needed for sharing.

Different same-name files and simultaneous edits keep a **conflict copy on both
PCs**. Replaced versions are retained under `.flowshare-versions` in the folder
where replacement occurred; these backups are excluded from sharing and sync.
Backups are not pruned automatically and use local disk space. To restore a
version, pause the link and copy the wanted `.bak` content back to its file.

**Deletions do not propagate.** The remaining copy can restore a locally deleted
file on the next sync. Renames behave like adding a new path while keeping the
old copy. Pause/remove the link before intentionally removing files from both
sides. This release synchronizes file contents and folders, not ACLs, alternate
streams, or filesystem metadata. It does not resume a partially sent file.

### Transfer speed

The Windows **Transfers** page shows measured transfer speed and estimated time
remaining. **Phone & browser** and **Shared Spaces** also show their active
aggregate speed. The browser's **Live transfers** section shows its uploads and
downloads, byte progress, speed, and remaining time; completed transfers retain
an average. Uploads also show progress beside the Send button. Estimates use
bytes actually streamed and change with network/storage conditions. They are
file-transfer rates, not a Wi-Fi link-speed measurement. Browser download
completion means the PC sent the data, not that the phone confirmed saving it.

### Optional access code

Enable **Require an access code** in Phone & browser, or save the same option
under Settings → Security & access. The current code is shown in Phone & browser.
It expires after five minutes and one use. New native connections and browser
sessions must enter it; already remembered PCs retain their saved-key access.
Use Unpair or Block in Devices to revoke a remembered PC.

Enabling/disabling code protection or choosing **New code** resets browser
sessions and cancels their active uploads/downloads. Browser sessions otherwise
last up to 12 hours, or until sharing stops/the PC app closes. Codes and session
secrets are memory-only; no private keys or codes are stored in UI preferences.

## Connectivity and files

Discovery/receiving and the optional browser listener start when the app opens.
Discovery uses mDNS plus dedicated LAN broadcast on UDP 48587. Remembered peers
are probed every ten seconds; saved-key checks remain mandatory if their address
changes. Active Wi-Fi/Ethernet subnets are accepted by native receiving. The app
recovers after adapter/address changes, deferring an unnecessary adapter restart
while transfers are active. Intentionally stopping sharing stays stopped.
Turn automatic direct connection off in Settings if desired.
Settings can disable automatic startup or browser sharing. If stopped, use
**Find nearby PCs** or **Start browser sharing**. Private-network firewall setup
is automatic by default. Approve the Windows administrator prompt when asked;
manual firewall rule entry is not required. The prompt may identify Windows
PowerShell, which runs the bundled app's fixed rule setup commands.

The rules cover this exact Lantoss executable, its current native/browser TCP
ports, and UDP discovery ports 5353/48587, restricted to Private networks and the
local IPv4 subnet. The helper verifies the resulting rules. A previous local
block for this executable has only its Private scope removed; Public/Domain
blocking is retained. Other applications and managed blocking rules are untouched.
The app never disables the firewall or changes the router, VPN, or network profile.

Settings can disable future automatic setup; existing rules are kept. Cancelling
administrator approval does not trigger repeated prompts. Retry with **Devices →
Connection diagnostics → Set up Private-network access**. Rules are checked on
opening when Windows permits inspection. If inspection needs administrator rights,
the app remembers a previously verified setup for the same executable and ports.

The phone page first tries port 80 for **http://share-wollvo.local**. If occupied,
it falls back to 48586, then an available port; use the current QR/link shown in
Phone & browser. Native receiving normally uses port 48585.
If native discovery is blocked, Manual connection remains a fallback. Check
router guest/client isolation, VPN routing, and whether the other PC is asleep.
**Devices → Connection diagnostics** shows listener/discovery state and detected
adapters, copies a report on request, and opens Windows network settings. Both
PCs must use a trusted network marked Private in Windows. Automatic setup does
not turn a Public network into Private or override router isolation or organization
policy. If setup cannot finish, its status and a retry action are in diagnostics.

Default inbox: `%USERPROFILE%\Downloads\Lantoss Inbox`; change it in Settings
or the receive-approval dialog. Files are never automatically opened. Clearing
history deletes metadata only. Closing the app or Stop sharing ends connections.
Forced termination/power failure can leave `.flowshare-*.part` files; normal
cancellation/failure cleans temporary files while keeping completed files.

## Limits and delivery status

- Windows x64 native app; modern local browsers use the phone page. No accounts,
  cloud service, telemetry, relays, public-IP connections, or automatic updates.
- Native: 256 top-level selections, 10,000 entries, 4 MB manifest, 1 TiB/file and
  2 TiB/job safety ceilings. Regular sends allow two outgoing/two incoming
  streams and eight outgoing jobs. Native space clients use three connection
  slots within eight accepted native connections. These are safety bounds.
- Browser: 100 uploads per batch, 2 GiB/file, 10 GiB/batch; up to 1,000 selected
  downloads, three active file streams, eight approved/pending batches, 64
  sessions. Native folder metadata/ACLs/links are not copied.
- Browser upload SHA-256 is recorded on the PC; it is not a comparison against a
  sender-provided hash. A download marked sent means the PC sent it, not that
  another device confirmed saving it. History labels browser HTTP separately.
- Up to 32 registered local spaces, with read-only or approved-upload access.
- Up to 16 sync links, 64 incoming sync grants, and 10,000 entries per linked
  folder. Sync state is stored separately in atomic `sync-state.json` snapshots.
- No transfer pause/resume, partial-file continuation, NAS agent, team roles,
  remote/internet sharing, or billing. Planned screens are labelled honestly.
- Unsigned development build. Physical phone/two-PC/router behavior, normal
  Windows key-store startup, notifications, and an independent security audit
  remain external validation items. See VERIFICATION.md for actual checks.

## Build and test

With an official .NET 10 SDK on Windows, run from this folder:

```powershell
.\app\Lantoss.exe
.\source\build.ps1 -Mode Build
.\source\build.ps1 -Mode CheckFormat
.\source\build.ps1 -Mode Test
.\source\build.ps1 -Mode TestLarge
.\source\build.ps1 -Mode Publish
node .\source\tests\browser-ui.test.cjs
```

`TestLarge` needs at least 24 GiB free for generated 10 GiB/1,000-file loopback
fixtures. Tests use memory identities and scratch folders. Optional arguments:
`-DotnetPath 'C:\path\to\dotnet.exe'` and `-BuildRoot 'C:\path\to\scratch'`.
NuGet restores official packages; runtime sharing needs no internet service.
The optional Node command runs browser input/state regressions with a DOM harness;
it is separate from the C# HTTP/TLS integration suite.

Architecture/security decisions: DEVELOPMENT_NOTES.md. Feature status:
ROADMAP.md. The old Hearth packages remain separate; their pages/codes do not
connect to this Lantoss portal.

## License

Lantoss is released under the [MIT License](LICENSE). Bundled third-party
components keep their own licenses; see the `*_LICENSE.txt` and notice files.
