# Verification record

## Lantoss 1.6 - 20 October 2026

**186 C# checks passed; one existing symbolic-link fixture was skipped** because
the Windows account lacks link-creation permission. The ten added checks cover
live System resolution, explicit overrides, unavailable Windows preferences,
text/action contrast in both palettes, persisted appearance and connection
preferences, fresh-install paths and reuse/priority of legacy metadata stores.
The existing native TLS, HTTP, file-integrity, shared-space, sync, discovery,
atomic-storage and mocked-firewall regressions also passed.

**18 native WPF UI checks passed** from the published 1.6.0.0 executable. They
cover populated device selectors in both themes, the Lantoss window title,
actual sidebar selection with immediate disk persistence, retaining the same
active page through appearance changes, System feedback and live action-label
contrast. All nine pages were exported in Light at 1440x900 and Dark at
1000x720. Home was also exported at 1440x960 in all three modes. Visual review
included Light/Dark Home, Dark Settings and populated Shared Spaces. These are
offscreen WPF renders with explicit preview mode, scratch metadata and memory
identities; normal LAN startup, Windows key-store changes and UAC do not run.

**11 browser DOM-state checks passed**, including the six existing upload/code/
shared-space/activity cases and five appearance cases: live system changes,
saved overrides, returning to System, blocked storage and invalid saved values.
JavaScript syntax, formatting and strict self-contained publication passed.
The packaged embedded browser assets loaded through the opt-in loopback preview.
A standalone headless Edge screenshot attempt did not produce an image and is
not counted as rendered browser or phone-device verification.

Real phone/two-PC/router behavior, Windows protected key-store startup, native
notifications and real UAC/firewall application retain the device-validation
limits described below. No real firewall rule or protected identity was changed
by these development checks. Historical checks below use the old FlowShare name.

Windows development checks, 18–19 October 2026. These are actual local checks,
not claims about a separate PC, router, production deployment, or security audit.

## FlowShare 1.5 firewall setup and populated selector fix

**176 C# checks passed**, with the same **one skipped symbolic-link fixture**.
Ten new checks cover device-name fallback, default/persisted firewall preference,
actual-port deduplication and validation, per-path rule/signature behavior, quoted
executable paths, exact Private/local-subnet/program/port scope, idempotence,
repair of prior local app denials while preserving Public/Domain/unrelated rules,
and managed-block rejection without modification. PowerShell setup scripts run
against mocked NetSecurity cmdlets in an unelevated child process; the tests do
not create or alter real Windows firewall rules. The installed New/Set cmdlets'
required parameter names were also checked read-only.

**Eight native WPF regressions passed** from the published 1.5.0.0 executable:
Shared Spaces selected name, compact selector height/explicit template, compact
refresh button, and Send selected name, each in Light 1440×900 and Dark 1000×720.
The synthetic PREVIEW device matches the reported NK-shaped record. The rendered
controls show NK and no internal Peer/fingerprint/flag text. These checks start
no listener or elevated helper and use memory-only identities and scratch data.
The six browser DOM-state checks and JavaScript syntax check also pass.

The UDP integration fixture now uses its own ephemeral test port, leaving the
production port 48587 unchanged. This avoids another running FlowShare instance
receiving the fixture's announcement; the first full run exposed that collision
and was fixed rather than treated as passing.

Formatting and strict self-contained Windows x64 publication pass. Release
packaging verifies all archived files against the published source/executable
and checks embedded browser assets with the explicit loopback preview. Real UAC
interaction and real firewall-policy application have not been exercised by
these automated checks; the runtime elevated helper verifies its actual resulting
rules before reporting success. Physical router/two-PC and protected key-store
validation limits below remain. No real firewall rules were changed in this
development session.

## FlowShare 1.4 native folder, sync, and connection update

The current suite exercises actual loopback native TLS/HTTP streams and files.
It passed **166 C# checks**, with **one skipped symbolic-link fixture** because
the Windows account cannot create test links. The new checks extend the prior
128-check suite; none of those native/browser safety cases were removed.
New coverage includes native folder listings/browsing, exact downloads and
duplicate keep-both, traversal and read-only rejection, one batch approval and
pinned upload destinations; declined/granted/persisted automatic sync; initial
merges and empty folders; edits in both directions; actual retained old versions;
simultaneous-edit conflict copies on both PCs; deletion preservation/restoration;
staging/version exclusions; revision-precondition rejection; pause/remove and
read-only/grant revocation; corrupted and interrupted sync bodies preserving
originals; scoped grant rejection; unpair grant/link cleanup; unattended background
sync and retries; host and client engine restarts resuming persisted permissions.
Unavailable local roots can be paused without recreating them; manual sharing of
a synced folder omits staging/version internals rather than failing on backups.

Discovery checks cover actual pinned-TLS automatic pairing after a discovery
observation, opt-out, code-protected peers, blocked keys, stale endpoint fallback,
subnet broadcast calculation, and a real UDP 48587 loopback announcement parsed
as an untrusted hint. Automatic-pair policy observations are injected through the
same internal handler used by both discovery services; this is not a physical
router/multicast/broadcast test. The UI's adapter-change monitor still needs
physical adapter/sleep/wake testing. No Windows firewall rule is changed.

The six existing browser DOM-state checks and JavaScript syntax check pass.
They remain DOM-harness checks, not rendered browser or phone-device QA.
The historical 10 GiB volume run was not repeated for this update. No claim of
fault-free operation on all routers, disks, or Windows policies is made.

Formatting verification and strict Windows x64 self-contained publication pass.
Version 1.4.0.0 exported all nine native pages in Light at 1440×900 and Dark at
1000×720. Review included native folder controls, Shared Spaces, Sync in both
themes, and the smaller Devices layout. The paused sync card was an explicitly
labelled PREVIEW fixture pointing to a real scratch folder; it is not bundled as
production data. Embedded browser HTML/QR loaded through the explicit loopback
diagnostic and the preview exited successfully. A populated remote file listing
is integration-tested through TLS; physical two-PC interactive UI is still an
external check. Packaging validates required source/license files, excludes
scratch/build data, and compares the archived executable hash to the published
binary. Normal Windows key-store startup, firewall/router interaction, and native
notifications retain the external-validation limits below.

## FlowShare 1.3 spaces and speed update

The current C# suite passed **128 checks**, with **one skipped symbolic-link
fixture**. It retains all 112 previous checks. New checks cover read-only
defaults/persistence, duplicate registration rejection, listings without local
paths, real files/subfolders, exact downloads, traversal denial, read-only
upload rejection before approval, approved writable uploads pinned to the
selected subfolder, revocation of approved uploads after permission changes,
disable/remove behavior without deleting files, missing-root availability,
actual streamed-byte speed/ETA, terminal average/exact totals, and session-only
transfer statistics. The speed fixture sends 2 MiB in paced chunks over a real
loopback HTTP connection; it is not a Wi-Fi benchmark.

**Six JavaScript UI-state checks** passed in the Node DOM harness, including
the earlier code-input retention cases plus space permission/destination
controls and current/average speed, ETA, and progress rendering. JavaScript
syntax also passes. A DOM harness is not a rendered browser or physical phone
test. The earlier browser-tool limitation and external checks below still apply.

Formatting verification and self-contained Windows x64 publication passed. The
published executable reports version 1.3.0.0 and exported **18 native screens**:
nine pages in Light at 1440×900 and Dark at 1000×720. Shared Spaces used a real
scratch folder and persisted registration under the PREVIEW label. Layout review
included the small top-bar buttons, both space cards, phone page, and the other
main pages. The explicit loopback browser diagnostic loaded the packaged HTML
and rendered a QR, then exited successfully; it did not use the Windows key
store or expose that preview on the home network. Release archive contents and
the executable hash are checked when packaging. The historical 10 GiB volume
run was not repeated for this update.

## FlowShare 1.2 direct/browser update

The updated C# suite passed **112 checks**, with **one skipped symbolic-link
fixture**. It used actual loopback HTTP/TLS sockets, sessions, streamed bodies,
and filesystem writes. New cases include direct native connection without any
comparison callback; optional-code enforcement and persisted policy; a real
browser listener that can be disabled independently; direct browser sessions;
selected-only listings, exact download bytes, Host/Origin/CSRF rejection;
unsafe names and oversized requests; decline-before-write; one approval per
multi-file phone batch; Unicode and empty uploads; single-use, session-bound
upload slots; keep-both duplicates; desktop/browser cancellation; partial-stream
disconnect cleanup; code-policy session revocation; wrong/valid/replayed and
rate-limited codes; changed-source and revoked downloads; honest HTTP history;
and stopping the actual listener. Existing native/data-safety checks also pass.

**Four JavaScript UI-state regression checks** passed in a Node DOM harness:
default direct entry, preservation during session refresh, preservation after
wrong codes/network failures, and entering sharing after valid optional login.
`node --check` also passed. This is not a rendered browser/device test. The
browser UI tool could not reach the temporary test page, so browser visual QA
and physical Android/iPhone behavior remain unverified.

The 1.2 source passes formatting verification and strict compilation. Release
checks cover the published executable, all nine native pages at both documented
sizes/themes, an explicit loopback-only browser/QR diagnostic loading embedded
assets from the packaged executable, and archive/executable hash matching.
Normal preview mode starts no listeners. The earlier 10 GiB volume run below
was not repeated for this feature update. Installed key-store creation and real
two-PC/router/native notifications remain external checks as listed below.

## FlowShare 1.1 connection update

On 19 October, the updated suite passed **72 checks**, with the same one skipped
symbolic-link fixture. It exercised real loopback TLS and filesystem transfers:

- Nearby capability discovery and older-client manual fallback selection.
- Matching-key approval on both PCs without typed bootstrap codes.
- Declines by either PC, public-key mismatches before approval, and hidden/blocked
  devices rejecting nearby requests without showing approval prompts.
- Persisted trust and the automatic-start preference's default/opt-out.
- A real file transfer after quick connection, with separate receiver approval
  and an exact-byte check; five-per-minute global approval-prompt limiting.
- All previous non-volume transfer, integrity, cancellation, and metadata cases.

The 1.1 build passes whitespace verification and strict compilation. The
published executable's eight screens are exported in Light and Dark at the
documented sizes and checked before packaging. Native first-window startup and
approval dialogs still need a normal Windows profile/two-PC check; offscreen
preview deliberately starts no listeners. Package contents and executable hash
are checked against the published binary. The earlier volume measurements below
were not repeated for this connection-only update.

## Transfer and data-safety checks

The earlier 1.0 full volume run passed **63 checks**, with **one skipped environment test**.
It used real TCP connections, TLS handshakes, pairing approvals, and filesystem
operations on loopback. Covered cases:

- Single-use/incorrect codes; declined and accepted pairing; matching comparison
  codes and persistent public-key bindings; actual receive-port recording.
- Unpaired and blocked devices; saved-key mismatch at the same address;
  unpair/block preventing future authorization.
- Receiver approval and decline; multiple files, nested paths, Unicode names,
  empty files and empty folders; exact original bytes and progress counts.
- Keep both, Skip, and explicit Replace; retries creating a new approved job.
- Path traversal/reserved names, malformed DNS packets, discovery source-IP
  checks, maximum-length Unicode device names, and manifest validation.
- Disk-space rejection, corrupt contents, mid-file disconnect, receiver
  cancellation, source changes, temporary cleanup, and integrity failures.
- Restarted metadata marking unfinished jobs failed; clearing history without
  deleting received files; graceful shutdown draining metadata writes.
- Deliberately stalled metadata storage: state reads and cancellation remained
  responsive, and the background writer persisted the latest cancelled state.

Symbolic-link creation was skipped because the Windows test account lacked the
required privilege. The code rejects reparse ancestors, but actual link/junction
escape tests need a Windows environment where test links can be created.

## Measured volume run

| Case | Actual data | Result | Recorded elapsed time |
| --- | --- | --- | --- |
| Many small files | 1,000 × 1,024 B | All files verified | 28.98 s |
| Large file | 10,737,418,240 B (10 GiB) | Sender/receiver SHA-256 acknowledgment and an additional independent destination hash matched | 253.25 s |

The large-file time includes preparation and independent hash verification.
Sampled process working set during that run was approximately **85–88 MB**.
These measurements came from one Windows machine, local storage, and loopback,
under uncontrolled development load. They are not Wi-Fi throughput benchmarks
or formal peak-memory measurements. Generated 10 GiB files were removed after
verification. Earlier runs exposed inactivity during storage pauses; the final
adapter uses async TLS record I/O, longer approved-stream/verification deadlines,
and background history writes. The final run passed rather than relabeling a
timeout as success.

## Build, UI, and package checks

- In 1.0, after the final cancellation/revocation and shutdown refinements, the
  non-volume suite passed **59 checks**, with the same one skipped link fixture.
- The C# projects compile with nullable checks and warnings treated as errors.
- The .NET formatter was run and its whitespace verification passes.
- Windows x64 publication bundles the .NET runtime into the executable.
- The published executable renders all eight screens in Light at 1440×900 and
  Dark at 1000×720. The smaller layout stacks cards and scrolls; dropdown text,
  navigation alignment, search prompts, and sidebar reachability were reviewed.
- Transfer-row rendering uses actual test metadata under a PREVIEW label.
  Production starts with no invented peers or transfer history.
- Hash and archive contents are checked when the release package is assembled.

## Checks still requiring another environment

- Transfers between two separate physical PCs over Wi-Fi/Ethernet, including
  a LAN with its internet connection physically disconnected.
- Live multicast discovery through the user's router/firewall, guest isolation,
  VPNs, adapter changes, sleep/wake, and other network hardware.
- Installed Windows certificate/key-store creation and reopening: the sandbox
  permitted reading but did not grant creating protected key files. Integration
  tests used memory-only identities and the same .NET RSA handshake signer.
- Actual symlink/junction escape fixtures, denied native folder-picker
  permissions, native notification delivery, and manual screen-reader use.
- Signing, installer deployment, independent security review, and macOS/Linux.

Per-person roles, cloud/remote sharing, transfer pause/resume, partial-file
continuation, and deletion propagation remain unimplemented. Local browser
sharing/QR were added in 1.2, folder Shared Spaces in 1.3, and native folder
browsing/two-way file sync in 1.4, with the
HTTP/access-control boundaries and actual test limits recorded above.
