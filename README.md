# Lantoss 1.6 for Windows and phone browsers

Private home-Wi-Fi file sharing. PC connections are direct by default: no
password, typed code, or comparison prompt. Access-code protection is optional.

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

1. Update **both PCs to 1.5** and keep both apps open. On the host, create a
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

The phone link normally uses port 48586. If occupied, a free port is selected;
always use the current QR/link. Native receiving normally uses port 48585.
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
