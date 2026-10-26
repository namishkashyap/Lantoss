# Developing Lantoss

Lantoss 1.9 is the renamed FlowShare application. It is a Windows x64 WPF app
with a native TLS transport and an embedded HTTP page for phones on home Wi-Fi.
The repository contains the complete source. Start with [SETUP.md](SETUP.md) for
clone-to-build instructions. Comments explain the boundaries and
invariants that are easy to miss when changing file, connection or sync code.

## Installer, Explorer and background lifecycle (1.8)

The per-user WiX MSI deploys the app, a native x64 COM DLL, logos, a signed sparse
MSIX and its registration script. Windows 11 uses IExplorerCommand plus the
windows.fileExplorerContextMenus and windows.comServer manifest extensions.
It runs through a COM surrogate; no managed CLR or networking runs on the menu
construction path. Windows 10 uses the old HKCU registry verbs as a fallback.

source/Explorer/LantossExplorer.cpp implements COM lifetime, IEnumExplorerCommand,
the always-present picker and saved-PC children. Menu construction reads a bounded
HKCU peer-name cache maintained by UI/WindowsIntegration. It never opens app data
or performs discovery. Peers are addressed by saved key fingerprint, never IP.
Blocking/unpairing is rechecked by the app when the selected command arrives.

Invoke receives IShellItemArray, writes one exclusive CREATE_NEW binary request
under LocalAppData/Lantoss/ShellRequests, then launches the exact adjacent app
with CreateProcessW. The format is LTSSREQ1, a 32-bit byte length + UTF-16 key,
a 32-bit item count and byte-length-prefixed UTF-16 absolute paths. The current
limit is 256 items / 96 KiB. Core/ShellSelection validates the directory, GUID
filename, regular-file ancestry, all lengths, UTF-16, recipient and paths. It
deletes the one-use request on close. No command shell parses filenames.

UI/ExplorerPackage handles signed sparse registration, removal and the explicit
development-publisher consent. The elevated path accepts no certificate path:
it can trust only the public certificate embedded into that particular app build.
The private PFX stays outside the source/release archive. A production publisher
must replace the development subject consistently and sign with a trusted code-
signing certificate. The current MSI is unsigned; native DLL and sparse MSIX are
development-signed. Publisher trust persists after uninstall so other Lantoss
installs using it keep working. It can be removed explicitly by thumbprint after
all these development builds are uninstalled.

The installer registers after files are copied and fails visibly if registration
or consent fails. Fresh-install rollback unregisters the new sparse package.
Feature removal/full uninstall unregister before removing files; upgrade removal
does not tear down a newer registration. Repair re-registers the staged signed
manifest. MSI/AppX upgrade rollback across already-successful package updates
still needs disposable-VM acceptance testing; these are separate transaction systems.

Core/DesktopInstance still routes validated requests through the same per-store,
CurrentUserOnly pipe/mutex. Named-PC sends use the existing transfer engine and
receiver approval; the picker prepares a visible draft without sending early.
--background and the tray retain the ordinary startup/server preferences.
Preview modes never register packages, trust certificates or start normal LAN
listeners. HomeAndSendPages.cs keeps Home focused on choosing files and devices.

## Find the right code

| Location | Responsibility |
| --- | --- |
| `source/Explorer/` | Native COM submenu, sparse manifest, signing/build scripts and isolated native tests |
| `source/Core/ShellSelection.cs`, `UI/ExplorerPackage.cs` | Bounded file-selection handoff and consent-based Windows 11 registration |
| `source/UI/HomeAndSendPages.cs` | Simple Home entry points and transfer selection UI |
| `source/UI/Program.cs` | Single-instance startup, legacy data selection and isolated preview modes |
| `source/UI/AppShell.cs` | Navigation, engine lifetime, UI dispatch, appearance and network recovery |
| `source/UI/Shell.xaml` | Shared WPF styles, sidebar and semantic resource bindings |
| `source/Core/Appearance.cs` | Light/Dark color tokens and testable System resolution |
| `source/Core/MetadataStore.cs` | Atomic preferences, paired keys, shared spaces and bounded history |
| `source/Core/AppDataPaths.cs` | Reuse an existing installation's complete data store through the rename |
| `source/Core/FileCatalog.cs`, `SpaceCatalog.cs` | Validate local roots, relative paths and permissions |
| `source/Core/Identity.cs` | Device certificate and comparison-code transcript |
| `source/Networking/TransferEngine.cs` | Pairing, pinned TLS and approved native transfers |
| `source/Networking/Wire.cs`, `ManagedTls.cs` | Bounded messages, streamed bodies and TLS channels |
| `source/Networking/MdnsDiscovery.cs`, `LanDiscovery.cs`, `ReliableConnections.cs` | Untrusted discovery hints, remembered peers and reconnect |
| `source/Networking/NativeSpaces.cs` | Authenticated native folder browsing and sync operations |
| `source/Networking/FolderSync.cs`, `Core/SyncScanner.cs`, `Core/SyncFiles.cs`, `Core/SyncStore.cs` | Cached snapshots, filesystem notifications, reconciliation, safe replacement, baselines and grants |
| `source/Networking/BrowserPortal.cs`, `BrowserSpaces.cs` | Local HTTP sessions, approvals and folder routes |
| `source/Networking/LocalDns.cs`, `LocalNameService.cs`, `LocalBrowserAccess.cs` | Bounded DNS codec, name conflict/lease state machine, mDNS responder and HTTP host/origin validation |
| `source/Core/SharedNotes.cs`, `Networking/TextSharing.cs`, `UI/NotesPage.cs` | Bounded text/link store, paired TLS sends and native board |
| `source/Browser/portal.*` | Responsive phone page, appearance, uploads and transfer speed |
| `source/Core/PrivateFirewallPlan.cs`, `UI/PrivateFirewallSetup.cs` | Scoped rule scripts, verification and Windows UAC |
| `source/tests/` | Filesystem, TLS, HTTP, sync, appearance and mocked-firewall regressions |

## Build and verify

Install the .NET 10 SDK on Windows. The executable includes its runtime; the SDK
is needed only to develop. Run these commands from the repository root:

```powershell
.\source\build.ps1 -Mode Build
.\source\build.ps1 -Mode Test
node --check .\source\Browser\portal.js
node .\source\tests\browser-ui.test.cjs
.\source\build.ps1 -Mode CheckFormat
.\source\build.ps1 -Mode Publish
```

Use `-DotnetPath` for a specific SDK executable and `-BuildRoot` for an alternate
intermediate directory. Node.js is needed only for browser regression checks.
NuGet restore performs the normal package vulnerability checks. `Publish` creates
the self-contained Windows x64 app in `app/Lantoss.exe`.

The explicit diagnostic modes use scratch metadata and memory-only identities:

```powershell
.\app\Lantoss.exe --ui-regression .\work\ui-checks --data-dir .\work\ui-state
.\app\Lantoss.exe --ui-smoke .\work\screens --data-dir .\work\smoke-state
```

Normal LAN startup and UAC are disabled in previews. `--preview-browser` is a
separate opt-in loopback-only diagnostic. Avoid launching a normal instance as
a substitute for these checks: it uses the user's actual device identity.

## Follow a transfer

1. Selection validation produces a bounded manifest of regular local files.
2. Discovery supplies an endpoint hint; TLS must still prove the expected key.
3. The receiving PC approves the manifest and chooses its local destination.
4. Bytes stream into a hidden temporary file. A SHA-256 check precedes atomic
   publication, and the receiver acknowledges the verified digest.
5. TransferRegistry publishes immutable progress snapshots; WPF events marshal
   through `AppShell.Ui`. Closing the app cancels work and drains history writes.

Do not remove path checks because a caller already validated an earlier step.
Permissions, roots and destinations can change while an approval or stream waits.

## Follow a sync cycle

Set up a folder pair from one initiating PC. The host's one-time permission is
bound to the paired key, sync ID and space ID. About every five seconds, the
coordinator checks cached local/remote snapshot revisions. Unchanged peers omit
the repeated manifest and avoid baseline clones and reconciliation dictionaries.
FileSystemWatcher invalidates hashes on edits, including an edit preserving size
and modification time. Event generations prevent a newer event on the same path
from being erased while older notifications drain. Directory events invalidate
their descendants. Overflow marks the root dirty and restarts the watcher;
unhealthy watchers retry after 30 seconds with metadata polling in the meantime.

Healthy roots reuse their immutable manifest for 60 seconds, then enumerate
metadata to catch missed notifications. One old file per sweep gets a checksum
audit after 30 minutes. Manual Sync now forces a full checksum pass on both PCs.
The scanner streams SHA-256 through one reader with a 64 KB buffer, keeps validated
hash progress across later locked-file failures, and never publishes partial
manifests. It bounds roots to 32 and cached hashes to 20,000, evicts cold roots,
and disposes watchers with the engine. Existing per-folder 10,000-entry limits
still apply. Missing roots are reported; never recreate them as empty folders.

A single-side edit propagates. Simultaneous differences preserve a deterministic
conflict copy on both PCs. Replacing a file retains the previous bytes in the
version directory. Revision preconditions reject a stale replacement. Deletions
are deliberately not propagated: the surviving copy can restore the path. Pausing
or revoking a link cancels current work and prevents future writes.

## Local names and browser routing (1.9)

BrowserPortal first binds HTTP port 80, then 48586 or an available port. Only a
port-80 listener with discovery enabled starts LocalNameService. It probes three
times before claiming share-wollvo.local and a per-key share-wollvo-<key>.local
name. LocalNameClaim is clock-driven for deterministic simultaneous-claim,
conflict, defense, lease and goodbye tests. Announcements renew short leases;
another eligible PC can take the common alias after the owner leaves. IPv4 A
answers include NSEC for absent AAAA; legacy unicast IDs/questions are retained.
Queries from this PC's own address are allowed so its browser can resolve names.

mDNS discovery advertises each PC's unique browser URL. Only the expected key's
local name, HTTP port 80 and no extra path/query are accepted into the browser
device picker. LocalBrowserAccess restricts Host and Origin to the actual local
addresses/names owned by that listener. An alias does not bypass sessions, CSRF
or authorization. Names are untrusted LAN discovery, not identity proofs.
Disabling discovery withdraws names. Loopback previews never start this service.
The optional test endpoint isolates multicast tests from production UDP 5353.

See [RFC 6762](https://www.rfc-editor.org/rfc/rfc6762.html) and the
[FileSystemWatcher documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0)
for the network and watcher semantics behind these recovery paths.

## Text/link board and folder refresh (1.9)

SharedNotes atomically stores the latest 100 notes within 2 MB separately from
settings, with 16,000-character / 64 KB input limits and revision-only unchanged
poll responses. Exclude shared-notes.json and its backup from source archives.
Native text-post RPCs run only after saved-key authorization. Browser posts need
a session, matching Origin and CSRF token, with a per-session rate limit. Render
only textContent/TextBlock and allow explicit link actions only for HTTP/HTTPS.
Incoming events refresh lists without replacing composer drafts or upload choices.

Browser folder refresh retains the selected space/path and ignores stale
responses after navigation; native Refresh queues a pending request when a load
is already in flight. Neither action resets the user to the space catalog unless
the current space is no longer available.

## Change appearance

Use semantic tokens rather than hardcoded page colors. WPF controls must use
`DynamicResource` or `SetResourceReference` so switching appearance updates the
current page without losing selections or cancelling jobs. Action foregrounds
use `OnAccent`, which differs from ordinary `Text` and changes in Dark mode.

The sidebar saves its choice immediately. System reads Windows AppsUseLightTheme
and refreshes on UserPreferenceChanged. Explicit Light/Dark ignores later Windows
changes. The browser uses its own matchMedia preference and localStorage choice;
storage failures must not prevent file sharing. Keep browser CSS tokens aligned
with Core/Appearance.cs and preserve the accessible text contrast checks.

## Compatibility names that must stay

Fresh installs store data under `%LOCALAPPDATA%\Lantoss` and default downloads to
`Downloads\Lantoss Inbox`. Existing FlowShare stores are reused in place, including
saved identity, receive folder, peers, history, space registrations and sync grants.
The old mutex namespace prevents old and renamed processes opening that same store
at once. Close the old app before launching Lantoss.

The mDNS service `_flowshare._tcp.local`, UDP discovery token,
`FlowShare pairing v1` transcript label and `.flowshare-` staging/version names
remain stable for existing peers and backups. These are protocol/storage contracts,
not missed UI branding. Changing them requires an explicit migration strategy.
Browser cookies and CSRF header now use Lantoss; reload an already-open old page.
Firewall rules are scoped to the new exact executable path, so Windows may ask
for administrator approval once after the rename.

## Preserve these boundaries

- An advertised device is not yet trusted. Prove its key through TLS.
- Never accept a caller's absolute destination or follow reparse-point roots.
- Recheck shared-space permission and sync grants during streaming and before commit.
- Keep host approval for manual transfers separate from ongoing approved sync.
- The phone portal uses HTTP. A code gates access; it does not add encryption.
- Elevation belongs only to the fixed, scoped firewall helper. Tests mock its
  cmdlets and must not modify real Windows firewall rules.
- Treat native UI renders and loopback integration as local verification. Real
  routers, phone devices, Windows key-store startup and UAC need device testing.
