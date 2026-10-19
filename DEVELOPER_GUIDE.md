# Developing Lantoss

Lantoss 1.6 is the renamed FlowShare application. It is a Windows x64 WPF app
with a native TLS transport and an embedded HTTP page for phones on home Wi-Fi.
The repository contains the complete source. Start with [SETUP.md](SETUP.md) for
clone-to-build instructions. Comments explain the boundaries and
invariants that are easy to miss when changing file, connection or sync code.

## Find the right code

| Location | Responsibility |
| --- | --- |
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
| `source/Networking/FolderSync.cs`, `Core/SyncFiles.cs`, `Core/SyncStore.cs` | Reconciliation, safe replacement, baselines and grants |
| `source/Networking/BrowserPortal.cs`, `BrowserSpaces.cs` | Local HTTP sessions, approvals and folder routes |
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
coordinator compares two SHA-256 snapshots and the last agreed baseline. Cached
hashes are invalidated by size/time changes and periodic rehashing.

A single-side edit propagates. Simultaneous differences preserve a deterministic
conflict copy on both PCs. Replacing a file retains the previous bytes in the
version directory. Revision preconditions reject a stale replacement. Deletions
are deliberately not propagated: the surviving copy can restore the path. Pausing
or revoking a link cancels current work and prevents future writes.

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
