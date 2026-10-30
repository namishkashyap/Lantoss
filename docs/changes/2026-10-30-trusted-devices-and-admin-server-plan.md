# 2026-10-30: Trusted devices and admin server (plan, not started)

This is the proposed feature list for review. Nothing is built until the list and the decisions below are approved.
Each step is its own small release, so each can be tested before the next starts.

## Part A: trusted phones and browsers

- [ ] **0.1 Remembered browser devices.**
  - A phone or browser keeps a long-lived device key in its own browser. The PC stores only a hash of it.
  - The same phone is then recognized after reconnecting, instead of appearing as a new visitor each session.
  - Name, picture and history stay attached to that device.
- [ ] **0.2 Mark a device as trusted.**
  - Devices → a browser device → **Trust this device**, or tick "Always accept from this device" in the approval window.
  - A trusted list in Devices, with **Remove trust** and **Block**.
  - Trust is set only on the PC, never by the phone itself.
- [ ] **0.3 Send without asking (trusted devices only).**
  - Files, photos and folders from a trusted phone or browser are saved straight to the PC with no approval prompt.
  - Untrusted devices still ask, as today.
  - Files go to the Inbox, or `Inbox\<sender>` when sender folders are on. No Shared Space is needed.
  - Safety limits stay: a per-batch size limit, free-space check, keep-both on duplicate names, and never opened automatically.
- [ ] **0.4 Phone page for trusted devices.**
  - The phone shows "Trusted by <PC name> · sends without asking" and uploads start immediately.
  - Supports photo, video and file pickers plus drag and drop on desktop browsers.
- [ ] **0.5 Sender log.**
  - Every received file records the sender's name, device, IP address, time, size and SHA-256.
  - History can be filtered by device or person, with CSV export.
  - A Windows/macOS/Linux notification appears for each auto-accepted batch.

## Part B: admin server and user servers

- [ ] **0.6 Server roles.**
  - Any Lantoss computer can be an **Admin server** or a **User server**. The default is a standalone server, which works as today.
  - One admin per network group.
- [ ] **0.7 Join the admin.**
  - A user server joins an admin with a one-time join code shown on the admin. The admin approves it once.
  - The link uses the existing pinned-key TLS connection.
  - The admin can remove a server at any time.
- [ ] **0.8 User servers with their own people.**
  - Each user server keeps its own phones, browsers, trusted devices and inbox.
  - Its users connect to it, not to the admin.
- [ ] **0.9 Activity reporting to the admin.**
  - Each joined server sends its sender log to the admin: who sent which file, from which device, to which server, when, size and hash.
  - It also sends its connected users and devices.
  - Reports queue while offline and are delivered when reconnected.
- [ ] **0.10 Admin console.**
  - All servers, all users and devices, and a searchable log of every transfer across servers, with sender details.
  - Filters by server, person, device and date, plus export.
  - The admin can also trust, untrust or block a device on any joined server.
- [ ] **0.11 Two app experiences.**
  - **Lantoss Admin** shows the console, servers and the full log.
  - **Lantoss** (users) shows its own sharing screens and the admin it belongs to.
  - Available for Windows, macOS and Linux.
- [ ] **0.12 Privacy notice.** User servers and phones show "This network is managed by <admin>. Transfer details are visible to the admin."

## Part C: finish

- [ ] **0.13 Security review** of trust, auto-accept, join codes and reporting, with fixes.
- [ ] **0.14 Tests, installers for all platforms, INSTALL.md update, release.**

## Decisions needed before starting

1. **What the admin sees:** only the details (who, what, when, size, hash), or also a copy of every file? This plan assumes details only.
2. **Admin app:** a separate app/installer, or one app with an "Admin mode" switch? This plan assumes one app with two modes, which is easier to keep in step.
3. **Trust level:** "send without asking" for trusted devices only, or also an option to accept from everyone on the network? This plan allows trusted devices only, because accepting from everyone lets anyone on the Wi-Fi drop files on the PC.
4. **Scope:** build Part A first (0.1–0.5) as Lantoss 2.1, then Part B as 2.2? Or both together?
