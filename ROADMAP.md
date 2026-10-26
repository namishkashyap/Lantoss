# Delivery status

## Included in Lantoss for Windows 1.6

Lantoss branding across the app and browser; teal/mint Light and Dark themes;
immediate System/Light/Dark appearance selection in the Windows sidebar and
browser header; live system preference following; existing-installation data
compatibility; explanatory source comments and a developer architecture guide.

Home, Send, Transfers, Devices, Phone & browser, History, Settings; local mDNS;
direct PC connection by default with saved public keys; optional access codes;
native TLS and checksum-verified file/folder transfer; receiver approval;
progress/cancellation/retry; duplicate policies; local metadata and themes.

Phone/browser support: local HTTP page, QR link, explicit PC-file downloads,
approved streaming uploads with progress, optional code/session access control,
CSRF/host checks, duplicate keep-both, and separate HTTP history labels. Browser
HTTP does not claim native encryption or sender-checksum verification.

Shared Spaces: persistent local folder registrations, read-only by default,
optional approved uploads into existing subfolders, real browser browsing and
downloads, availability checks, and revocation on stop/remove/permission change.
Removing a space preserves its files. Permissions apply to all browser sessions.
The top bar and each space have Open in browser buttons. Native/browser transfer
views show measured speed and estimated remaining time; browser completion
records include measured averages.

Native Shared Spaces: browse another paired PC's shared folders inside the app,
download verified files, and send an approved upload batch into its chosen folder.
Automatic two-way sync: persisted links and one-time per-peer/space grants,
five-second reconciliation, offline retries, restart recovery, checksum revision
checks, conflict copies on both PCs, previous-version backups, and pause/remove/
revoke controls. Deletions do not propagate; backups are not pruned automatically.

Connection automation: dedicated UDP LAN discovery alongside mDNS, automatic
direct pairing with an opt-out and optional-code enforcement, authenticated
remembered-peer probes, address fallback without changing the key pin, and
adapter-change recovery. A native diagnostic view opens Windows network settings;
it opens Windows settings and reports automatic firewall-setup status.

Private-network firewall setup: automatic rules after Windows UAC approval,
exact executable and actual TCP ports, UDP discovery ports, Private/local-subnet
scope, idempotent updates, previous app-denial repair preserving Public/Domain
blocks, cancellation deferral/retry, and a settings opt-out. No global firewall,
network-category, router, other-application, or managed-policy changes.
PC dropdowns render only names, honor custom display templates, and remain compact.

Legacy comparison pairing/manual code fallback remains available. Discovery and
browser receiving can start on opening; both have user controls. Actual tests
and physical-device limitations are in VERIFICATION.md.

## Not implemented

Pause/resume and restart continuation; recipient batches/priorities/bandwidth
controls; global auto-accept of manual incoming files; launch at login; persistent background
receiving; Explorer sharing; compression; folder archives for browser download.

Per-person team roles, invites, audit administration, deletion propagation,
scheduled sync, backup pruning/restore UI, block-level sync, and NAS/mounted shares.

HTTPS setup for ordinary phone browsers, expiring internet share links, remote
relays, SSO, organizations, signing/installers, and native macOS/Linux clients.
No automatic router exposure or certificate-warning bypass is provided.
