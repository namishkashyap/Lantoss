# Engineering and security notes

## Lantoss 1.6 rename and appearance

See DEVELOPER_GUIDE.md for the current source map and maintenance workflow.
Appearance uses semantic WPF DynamicResources so active pages update in place.
Light/Dark override Windows; System resolves AppsUseLightTheme and listens for
UserPreferenceChanged. Browser appearance is per-client using matchMedia and
resilient localStorage, with a CSS media-query fallback before JavaScript loads.
Both palettes keep ordinary/secondary/action text at least 4.5:1 contrast on their
intended surfaces. Explicit OnAccent avoids white-on-mint labels in Dark mode.

AppDataPaths reuses an existing FlowShare store, including recovery metadata.
Its mutex namespace remains stable, as do the discovery token/service, comparison
transcript label and hidden staging/version prefixes. UI, executable, assembly,
resources, browser session/header and firewall rule branding use Lantoss.
Historical notes below retain the original product name.


## Current 1.5 Private firewall setup and PC selector fix

The user explicitly authorized automatic firewall setup on Private networks.
PrivateFirewallPlan emits fixed PowerShell commands for the current executable,
one/two actual listening TCP ports, and UDP 5353/48587. Rules are inbound Allow,
Private only, LocalSubnet4, and EdgeTraversalPolicy Block. Rule names are stable
per executable path; port changes update those same two protocol rules. Paths
are single-quoted with apostrophes escaped, then sent via EncodedCommand to the
system PowerShell executable. The privileged helper runs only through Windows
runas/UAC after normal startup, never during previews or integration tests.

The helper can remove Private scope from existing local inbound Block rules
for that exact executable, retaining their Public/Domain scope. It does not
touch other programs, managed blocks, global firewall profiles, router settings,
or the Windows network category. It verifies exact active rules and reports
failure if a managed app-specific block remains. Rules configured is not proof
of connectivity through every router or policy. A read-only check avoids repeat
elevation; if a standard user cannot read rules, a prior elevated self-verification
is remembered for the same path/port signature. Cancellation/failure is deferred
until explicit retry. Settings can opt out of future automatic setup.

The reported UI bug came from the custom ComboBox ContentPresenter omitting
ItemTemplateSelector, so its selected Peer fell back to the record's generated
ToString. The shared style now forwards ItemTemplateSelector/StringFormat;
Shared Spaces has an explicit name/ellipsis template and a 40px selector. Peer
ToString also returns Name as a privacy-safe fallback. Populated offscreen
regressions use the reported NK-shaped fixture in both themes, including Send.

Primary references: [WPF selection templates](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.combobox.selectionboxitemtemplate?view=windowsdesktop-10.0),
[Windows firewall rule scope](https://learn.microsoft.com/en-us/powershell/module/netsecurity/new-netfirewallrule?view=windowsserver2025-ps).

## Native folders, sync, and discovery (introduced in 1.4)

Native space RPCs run inside the existing mutual-key TLS transport after paired
peer authorization. Listings contain IDs and relative paths, never source roots.
Native uploads use a fixed space destination and one batch approval. Receivers
hash streamed bytes, save atomically, and acknowledge the original digest.
Three outgoing space slots and eight native accepted connections bound resource
use. Browser HTTP remains separate.

Two-way sync uses a single initiating coordinator per folder pair. The host
stores an explicit grant bound to sync ID, paired key, and space ID; a writable
space and one-time host approval are required. Sync reads and writes recheck the
grant; per-chunk authorization and pre-commit checks reject revoked access.
Client links, baselines, and host grants use separate atomic sync-state.json
snapshots with previous-copy recovery. Paused/removed links cancel active work.
Both apps must run; this is not a background Windows service.

The coordinator reconciles local/remote SHA-256 snapshots about every five
seconds and retries errors with bounded exponential backoff. A cached hash is
invalidated by size/time changes or a one-minute rehash interval. No watcher
event is required for correctness. Empty folders are merged, not just files.
A revision precondition rejects stale replacements; actual old contents are
retained by atomic File.Replace backups under excluded .flowshare-versions.
Simultaneous edits and initial same-path differences preserve a deterministic
remote-hash conflict copy on both PCs before replacing the canonical remote copy.
The first coordinating PC's current content remains canonical in that case.
Crash/partial-stream failures leave originals intact or preserve prior versions;
committed files survive and are reconciled on retry. This is file-level sync,
not partial-byte continuation, deletion propagation, or automatic backup pruning.
An adversarial local process racing same-account filesystem operations remains
outside the remote-path threat model, as for the original transfer path.

Discovery adds UDP 48587 broadcast using the existing bounded DNS announcement
parser alongside mDNS. Both are untrusted hints; automatic direct pairing still
proves the advertised key through TLS. Automatic connections respect opt-out,
code protection, hidden PCs, blocks, and connection-rate limits. Remembered PCs
are probed every ten seconds; a failed old endpoint may fall back to a discovered
endpoint with the same pin. Native listening binds IPv4 Any and accepts only
active selected Wi-Fi/Ethernet subnets and private IPv4 sources. The UI's network
monitor restarts listeners on adapter changes and honors an intentional Stop.
No subnet-wide TCP scan or router change is included. The separate 1.5 helper
adds the explicitly authorized scoped firewall setup described above. Diagnostics
report the actual selected addresses, listener state, and firewall-setup status.

Relevant platform behavior: [FileSystemWatcher event limitations](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-10.0).
Polling plus periodic rehashing avoids relying on those events for convergence.

## Spaces and transfer metrics (introduced in 1.3)

<!-- The section below records the earlier browser-only space implementation. -->
SharedSpace records extend schema 1 additively. Only the host can register up to
32 existing local folders. New spaces default to ReadOnly; ReadAndUpload still
requires a host batch approval. Browser callers supply a space ID and a relative
path. Root/ancestor/reparse validation confines browsing, downloads, and uploads
to the selected root. Upload approval cannot redirect a space destination to
another folder, and commit rechecks permissions. Listings omit staging names,
local root paths, and reparse entries, with a 1,000-entry per-folder bound.
Unavailable roots are reported, never recreated for browsing.

Changes to space permissions, disabling, and removal cancel active downloads and
revoke approved upload offers. Post-approval access checks reject revoked spaces.
Removing a registration does not remove contents. Registrations persist, so
uploads into a shared root are visible as part of that space. Inbox uploads are
not shared automatically. There are no per-person roles, delete/rename APIs,
watchers or remote/cloud access in the browser path. Native folder sync was added in 1.4.

Browser transfer records are mapped to their memory-only browser session; the
metrics endpoint returns only that session's recent jobs. Current rate comes
from actual transfer byte samples; remaining time uses remaining bytes/rate.
Terminal average uses the first streamed byte's start time through completion,
excluding approval time. The XHR upload view samples loaded bytes separately.
Neither estimate is a Wi-Fi link rate or a remote-save acknowledgment. The native
top-bar browser button starts enabled local browser sharing if needed and opens
its actual current URL. Space buttons add only a space ID to that URL.

## Connection and browser behavior (introduced in 1.2)

The user explicitly requested direct connection by default and optional code
protection. `RequireConnectionCode` therefore defaults false. A new
`pair-direct` request binds the actual TLS public key without showing either
comparison dialog. Subsequent connections pin that saved key. These records
have `KeyVerified=false` and the UI says saved key, rather than claiming a
human comparison. `direct=1` and `locked=0/1` discovery fields are capability
hints only. Enabling code protection gates new direct requests with the existing
expiring, single-use bootstrap code; it also gates/revokes browser sessions.
Remembered native peers must be unpaired/blocked to revoke their saved access.
Legacy manual/comparison pairing below remains available. The hidden-discovery,
blocked-key, request-rate, and file-approval checks remain active.

The phone portal uses ASP.NET Core 10 Kestrel bound to the selected private IPv4
address. It normally uses port 48586, with a free-port fallback. QRCoder 1.8.1
generates a QR containing that URL, never a password/code. Local embedded HTML,
CSS, and JavaScript contain no CDN or external tracking dependencies.

This path is deliberately labelled **HTTP**: browsers on ordinary phones cannot
trust a self-signed local certificate automatically. No certificate-warning
bypass is requested. A code is access control, not HTTP confidentiality. Native
TLS remains separate. With code protection off, reachable LAN clients may
download the PC files and browse the folder spaces the user explicitly selected.
Other disk locations remain inaccessible.

Browser sessions use random memory-only tokens, HttpOnly/SameSite=Strict cookies,
12-hour expiry, source-IP/generation binding, exact Host validation, and Origin
plus CSRF-token checks on mutations. Protection changes reset sessions and
cancel active offers/downloads. Session/offer/request/connection bounds prevent
unbounded allocation. JSON control bodies have a 32 KiB limit even when chunked;
raw uploads are streamed in 64 KiB buffers after a single local batch approval.
Up to 100 files/10 GiB per batch, 2 GiB/file, 8 pending/approved batches, 64
sessions, 1,000 selected downloads, and 3 streams are supported safety bounds.

Uploads validate Windows filenames, free space, lengths, session ownership,
single-use slots, cancellation, and destination ancestors. Files go to staging,
flush, then move atomically with keep-both naming. SHA-256 is recorded locally;
it is not a sender-provided browser checksum. Browser records explicitly use
`Browser · HTTP` and do not claim native hash acknowledgment. Downloads use
attachment headers, selected-file IDs, safe paths and source-change checks.
They report bytes sent, not a remote-save acknowledgment. Received files never
become browser downloads unless the PC user explicitly adds them or approves
uploading them into a registered shared space.

Preview mode remains listener-free unless the explicit `--preview-browser`
diagnostic flag is given. That diagnostic uses memory identities and loopback
only, checks the packaged embedded HTTP page, renders a live QR, then stops the
listener on exit. It neither creates Windows keys nor exposes files on the LAN.

Primary implementation references: [Kestrel endpoints](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0),
[streaming uploads](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-10.0),
and [QRCoder 1.8.1](https://github.com/Shane32/QRCoder/releases/tag/v1.8.1).

## Architecture

The existing C#/WPF stack is retained and upgraded to .NET 10. Core models,
metadata, source/receive planning, identities, transfer state, mDNS, TLS, and
native UI components are separated. Windows is the confirmed target.

The implementation sequence was: core identity/transfer/data-safety services;
discovery/manual connections and persistence; separate reference-inspired
screens; actual loopback and layout checks; Windows packaging. Future features
stay gated. Production invents no devices, transfer speeds, quotas, or history.
Offscreen rendering is labelled PREVIEW. Only the explicit loopback browser
diagnostic flag starts a listener during export.

Schema 1 stores metadata in atomic JSON snapshots (`.new`, replace, `.bak`).
Contents never enter JSON or UI state. Public thumbprints and key fingerprints
are metadata. Unknown schemas fail rather than being overwritten. Interrupted
jobs reopen as failed, not completed. History is capped at 300 recent records
and a 16 MB snapshot budget. JSON was retained for the existing small project;
SQLite was suggested for a new project, rather than being required here.
History saves coalesce on a background writer. Disk I/O holds no state-reader
lock, and cancellation/state inspection stay responsive during a slow metadata
flush. Graceful shutdown closes connections, awaits workers, and drains pending
history writes with a bounded timeout. Forced termination can leave temporary
files; crash-resume and automatic orphan cleanup are not implemented.

## Encryption and identity

Transport uses BouncyCastle.Cryptography 2.7.0: TLS 1.2, ECDHE-RSA, AES-GCM,
and extended master secret. Encryption cannot be switched off. This is the
maintained library's TLS implementation, not a custom encryption algorithm.
Its nonblocking record API is driven by native async socket I/O.

The installed 3072-bit RSA identity is held in the current user's Windows
Personal certificate/key store, never a trusted-root store. .NET RSA signs
handshakes, allowing the installed private key to remain non-exportable. The
temporary PKCS#12 bytes used at initial import are cleared afterward. Private
keys never enter frontend storage, metadata, pairing messages, logs, or the
clipboard. Tests use memory-only identities; the restricted environment did
not grant creation of protected Windows key-store files. Installed-store
creation/reopening therefore remains an external validation item.

Peer trust pins SHA-256 of SubjectPublicKeyInfo. There are no certificate-chain,
AIA, OCSP, revocation, relay, account, signaling, telemetry, or update requests.
Only private IPv4 destinations and local multicast DNS are used.

## Legacy comparison pairing protocol v1

FlowShare 1.1 adds an optional `pair-nearby` request. mDNS TXT `quick=1` advertises
support; this flag and all discovery fields remain untrusted hints. The initiator
pins the advertised public key for the TLS handshake, then both humans compare
the code derived from the actual TLS keys and approve. No typed bootstrap token
is required for this path. A declined/mismatched request never grants access.
Hidden devices reject this mode. Incoming nearby requests are bounded by the
existing per-IP limit, a global five-per-minute limit, one pending approval, and
the two-minute deadline. Receiving files still requires separate approval.

The original address/code `pair` request remains the manual fallback:

1. Both PCs prove certificate private-key possession in a fresh TLS handshake.
   An unpaired identity can attempt pairing but cannot submit a file transfer.
2. The initiator supplies the other PC's single-use, five-minute, 50-bit code
   and a fresh nonce. Codes live only in memory and attempts are rate-limited.
3. Both screens display a comparison derived from both full public-key
   fingerprints and the nonce. Users compare the 48-bit displayed value and
   approve on both PCs before bindings are saved. Names/IPs are not trust.

mDNS advertises only a service/version, name, public-key hint, and endpoint on
`_flowshare._tcp.local`, IPv4 224.0.0.251:5353. No filenames are advertised.
Advertisements grant no authorization. A saved peer's recently-seen status is
updated by authenticated probes, rather than a claimed discovery identity.
Blocked multicast leaves manual pairing available.

The native main window starts the listener/discovery on its first Loaded event,
using the preferred private IPv4 adapter. `StartReceivingOnOpen` defaults true
and is persisted; the user can turn it off in General. Failures remain visible
with recovery controls, without changing firewall settings. Preview exports do
not load/show the native window and do not start networking. Saved identities
are probed on listener startup, so remembered PCs can reconnect even when their
name announcements are unavailable and the stored private address still works.

## Transfer protocol v1

Control frames contain a four-byte big-endian length and bounded UTF-8 JSON
(4 MB maximum). An authenticated sender submits a validated manifest of
relative names, entry types, sizes, and SHA-256 hashes. Absolute source paths
and modification times stay local. The receiver approves before file bytes
are accepted; folder and duplicate decisions come from local UI.

Files stream through SHA-256 into temporary `.flowshare-*.part` files under the
chosen destination. The receiver flushes, verifies, then moves/replaces
atomically. Each verified file returns its hash; the sender requires every
expected acknowledgment and the final completion response before reporting
success. Skipped files are counted, not claimed verified against the source.

Validation rejects traversal, absolute/drive paths, reserved names, invalid
Windows characters, case-insensitive conflicts, missing parents, and reparse
ancestors. Checks repeat before creating/committing files. A hostile local user
with the same filesystem privileges is outside this remote-path threat model;
use a destination controlled by your Windows account. Disk space, frame sizes,
entry counts, connections, queued jobs, and concurrent streams are bounded.

Cancellation and revocation close connections. Partial files are cleaned;
verified files stay. Revocation cannot erase copies already received. Replace
requires an explicit recipient choice and confirmation. Source changes are
detected before streaming and receiver checksums catch changed bytes.

Unauthenticated handshakes/control frames have short deadlines. Approved file
streams allow longer storage/network inactivity, and verification acknowledgment
allows disk flush time. There is no silent reconnect/resume or false completion.

## Native UI and privacy

Received executables are never run; files are never automatically opened or
shared. Clipboard access requires a Copy action. CSV exports omit contents,
paths, addresses, codes, and keys, and escape formula-like cells. Opening Sync
does not watch or share folders. Progress comes from backend bytes.

The app uses native pickers, Explorer reveal, Windows completion notifications,
keyboard controls, accessibility names, and system theme detection. No loading
animations are required. Phone/browser sharing and its QR link are functional;
Shared Spaces and native Sync are functional; per-person roles, global auto-accept, and billing remain planned. Hearth packages
remain separate.

## Primary references

- [Bouncy Castle C# distribution](https://www.bouncycastle.org/download/bouncy-castle-c/)
- [TLS nonblocking API source](https://github.com/bcgit/bc-csharp/blob/release-2.7.0/crypto/src/tls/TlsProtocol.cs)
- [Microsoft X509Store API](https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509store)
- [Multicast DNS, RFC 6762](https://www.rfc-editor.org/rfc/rfc6762)

Actual checks, development failures repaired, and validation limits belong in
VERIFICATION.md. No independent security audit or cross-device benchmark is implied.
## 1.9 implementation notes - 22 October 2026

Local names are implemented inside the app, with an isolated DNS codec/claim
state machine and a selected-adapter mDNS responder. Production needs HTTP port
80 and discovery enabled. Common-name conflict handling and per-key URLs keep
multiple PCs usable; the browser picker exposes each PC. Host/Origin/session
checks remain in force. QR fallback explains port/resolver/network limitations.

SharedNotes is a separate bounded atomic store. Native text sends use existing
pinned TLS authorization; browser posts use its session/Origin/CSRF policy.
Neither renderer evaluates received text or automatically opens/copies it.
Refresh paths retain the current shared subfolder and ignore stale completions.

SyncScanner now owns/disposes watchers and caches immutable snapshots. Generation
stamps preserve concurrent events, overflow restarts watchers, minute metadata
sweeps recover missing events, and rolling old-file checks avoid whole-tree idle
rehashes. Manual Sync now forces hashes. Remote revisions and cheap grant/header
reads avoid repeated manifest bodies and deep baseline copies on idle cycles.
Read DEVELOPER_GUIDE.md and VERIFICATION.md for boundaries, metrics and test scope.

