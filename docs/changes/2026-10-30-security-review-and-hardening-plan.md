# 2026-10-30: Security review and hardening plan (plan, not started)

This is a read-only review of the whole app. It covers:
- the browser portal server and the phone page
- the PC-to-PC TLS protocol, discovery, spaces and sync
- local storage, IPC, the Explorer menu, the installers, packaging and the supply chain

Nothing was changed. The items below are proposed fixes for review. Nothing is built until they are approved.
Line references are from branch release/2.0.1, commit d5ac064.

## Verdict

- **From the internet: not reachable today.**
  - The listeners bind only to the LAN address.
  - The portal drops any client outside the private subnet (`BrowserPortal.cs:381`).
  - The firewall rule allows only Private networks and the local subnet.
  - So the app fails closed against outsiders.
- **On the same Wi-Fi: weak defaults.** Out of the box, anyone on the same network can do the following without approval:
  - pair a PC silently
  - open the phone page
  - read every enabled shared space
  - post notes

  These are the first things to fix.
- **Online access needs a new design.** The current LAN checks (private IP only, per-IP limits, plain HTTP, mDNS) cannot simply be opened to the internet. Part B lists what online access needs instead.
- **Already strong:**
  - TLS with pinned keys, mutual authentication and constant-time checks
  - 50-bit single-use access codes
  - 256-bit session and CSRF tokens
  - a strict CSP with no innerHTML in the phone page
  - thorough path-traversal, reserved-name and junction checks
  - SHA-256-verified transfers
  - sync never propagates deletes
  - size-capped JSON stores with no type-name deserialization
  - a per-user pipe with CurrentUserOnly
  - a firewall rule limited to the Private profile and the local subnet
  - build tools pinned by SHA-256

  Do not redo these.
- **No app can promise "cannot be hacked".** The goal is layered defence: each layer stops attacks on its own, so one bug is not enough to get in. Online launch also needs an outside penetration test (0.25).

## Part A: fix on the home network first

| # | Severity | Item |
|---|---|---|
| 0.1 | High | Pairing needs approval by default |
| 0.2 | High | Phone page needs approval by default |
| 0.3 | High | Encrypted phone page (HTTPS) |
| 0.4 | Medium | Who can see each shared space |
| 0.5 | Medium | Hidden-character name spoofing |
| 0.6 | Medium | Mark received files as "from another device" |
| 0.7 | Medium | Flood and lock-out protection |
| 0.8 | Medium | Stores and memory that can't be filled up |
| 0.9 | Low | Disconnect and block take effect immediately |
| 0.10 | Low | No internal details sent to other devices |
| 0.11 | Low | Fake PCs and hijacked .local names |
| 0.12 | Low | Safer "Open link" |
| 0.13 | Medium (Linux) | Private data files on macOS and Linux |
| 0.14 | Low | Small hardening |
| 0.15 | Low (High online) | Stronger pairing comparison |
| 0.16 | Info | Less device information broadcast |

- [ ] **0.1 Pairing needs approval by default.**
  - Today, with the default settings, `TransferEngine.cs:570` accepts a direct pairing with no dialog.
  - Auto-connect dials any PC that announces itself (`ReliableConnections.cs:50`).
  - An attacker on the same Wi-Fi becomes a trusted PC within seconds. It can then browse and download every enabled shared space.
  - Fix:
    - The first pairing with any new PC always needs an on-screen confirmation on both PCs, or the access code.
    - Auto-connect only reconnects PCs that were already paired. Nothing new is dialled automatically.
    - This also stops discovery being used to make the app connect to arbitrary LAN addresses.
- [ ] **0.2 Phone page needs approval by default.**
  - Today, a plain `GET /api/session` gives any device on the Wi-Fi a session when no code is required (`BrowserPortal.cs:417`).
  - That session can list and download shared files and spaces, and read and post notes.
  - Fix:
    - A new phone or browser must be approved once on the PC, or enter the code.
    - Reading files, spaces and notes then requires that approval, not just sending.
    - This is the base for the trusted-devices feature (plan 0.1–0.3).
- [ ] **0.3 Encrypted phone page (HTTPS).**
  - Today the phone page is plain HTTP. On shared or open Wi-Fi, someone can read the access code, the session cookie, notes and files, or swap a download.
  - Fix:
    - Serve the portal over HTTPS with the PC's own certificate.
    - Mark the cookie `Secure` and give it the `__Host-` prefix.
    - Add HSTS.
  - Browsers show a one-time warning for a home-made certificate (see decision 2).
- [ ] **0.4 Who can see each shared space.**
  - Today every paired PC can read every enabled space (`NativeSpaces.cs:212`).
  - Fix: each space gets a list of allowed PCs and phones, checked on every request. The default is "only devices I choose".
- [ ] **0.5 Hidden-character name spoofing.**
  - Today, file names and PC names allow Unicode direction-override characters (`FileCatalog.cs:129`, `MetadataStore.cs:184`).
  - `invoice‮fdp.exe` then shows as `invoiceexe.pdf` in the approval window, the phone page and Explorer.
  - Fix:
    - Reject format characters (Unicode category Cf) the way visitor names already do (`Profiles.cs:44`).
    - Warn when two devices share a name.
    - Show a short device fingerprint next to names in approval windows.
- [ ] **0.6 Mark received files as "from another device".**
  - Today, received files have no Mark-of-the-Web. Programs, scripts and Office macros from a phone or PC run without SmartScreen or Protected View.
  - `.url`, `desktop.ini`, `.library-ms` and `.searchConnector-ms` files placed in a space can leak Windows sign-in hashes when the folder is browsed.
  - Fix:
    - Write `Zone.Identifier` (ZoneId=3) on every received file.
    - Show a warning badge on programs, scripts and shortcuts.
    - Block shell-control files in spaces.
- [ ] **0.7 Flood and lock-out protection.**
  - Today, the following limits are global and shared by every device:
    - 24 portal connections
    - 3 portal streams
    - 8 pending offers
    - 8 TLS slots
    - 64 sessions
    - 128 login-limit IPs
    - 100 notes
  - One device can therefore fill them and lock everyone out. It can also flood the PC with approval prompts.
  - Fix:
    - Limits per device or IP, plus reserved slots for already-paired PCs.
    - Shorter timeouts before sign-in.
    - A cooldown after a declined offer, and a timer that clears expired offers.
    - A per-device quota for notes and text, with PC-written notes never evicted.
    - The least recently used session is evicted instead of refusing new ones.
    - Stale discovery entries are removed after 75 s.
- [ ] **0.8 Stores and memory that can't be filled up.**
  - Today:
    - Adding the 257th PC breaks every later pairing save (`MetadataStore.cs:198`).
    - Finished jobs and in-memory history are never released (`TransferRegistry.cs:173`).
    - Sync backups (`.bak`) are never pruned (`SyncFiles.cs:52`).
    - Forced rescans and rehashes have no rate limit (`NativeSpaces.cs:233,276`).
  - Fix:
    - Check limits before changing data, and evict unverified records first.
    - Remove finished jobs and trim history.
    - Keep backups by count and size.
    - Rate-limit rescans and allow forced rescans only with a sync grant.
    - Re-validate peers, spaces and preferences when the stores load.
- [ ] **0.9 Disconnect and block take effect immediately.**
  - Today, a disconnected phone keeps downloading the current file (`BrowserPortal.cs:349-367, 875`).
  - Fix: cancel that device's transfers, and add a block list so it can't reconnect.
- [ ] **0.10 No internal details sent to other devices.**
  - Today, error messages sent to phones include full local paths and the Windows user name (`BrowserPortal.cs:607`, `FileCatalog.cs:151`).
  - A peer's own error text is shown on this PC as a status (`Wire.cs:100`).
  - Fix: send generic messages and keep the details in the local log. Map peer errors to our own wording.
- [ ] **0.11 Fake PCs and hijacked .local names.**
  - Today, two spoofed mDNS replies make the PC give up its custom name. About 32 make it stop claiming names until restart (`LocalDns.cs:214, 257-298`).
  - A fake PC can appear in the phone's "Other PCs" list and serve a copy of the page that collects the code.
  - Fix:
    - Never give up the name permanently.
    - Show the PC's IP address and fingerprint on the code screen.
    - Mark unverified PCs in the list, or list only paired PCs.
- [ ] **0.12 Safer "Open link".**
  - Today the raw note text goes to the shell (`UI/NotesPage.cs:61`, `Desktop/PhoneNotesPages.cs:189`).
  - Fix:
    - Open only the parsed `https`/`http` address.
    - Show the host name and ask before opening.
    - Warn on links to LAN host names.
- [ ] **0.13 Private data files on macOS and Linux.**
  - Today, `metadata.json`, `sync-state.json` and `shared-notes.json` are created with the default permissions 0644, in a folder with 0755. Other local users on Linux can read history, paired PCs and shared text.
  - The temporary key file reuses old permissions (`Identity.cs:77-82`). The macOS/Linux app has no single-instance lock.
  - Fix:
    - Data folder 0700 and files 0600, applied to existing files on startup.
    - Create the key file fresh each time.
    - Add a lock file so only one copy runs per data folder.
- [ ] **0.14 Small hardening.**
  - Compare CSRF tokens in constant time (`BrowserPortal.cs:375`).
  - Add the `Permissions-Policy`, `Cross-Origin-Opener-Policy` and `Cross-Origin-Resource-Policy` headers, plus Trusted Types.
  - Reject profile pictures over about 1024 px before decoding.
  - Add the session ID to the pipe name and back off on failure (`DesktopInstance.cs:13`).
- [ ] **0.15 Stronger pairing comparison.**
  - Today, an attacker in the middle with a GPU cluster could search for a matching 12-character comparison code within 2 minutes (`Identity.cs:106`).
  - Fix: each side commits to its random value before seeing the other's. Alternatively, use the code as a password-authenticated key exchange bound to the TLS channel. Required before online pairing.
- [ ] **0.16 Less device information broadcast.**
  - Today, TLS 1.2 sends both certificates in clear text, and discovery broadcasts the full fingerprint and name every 10–20 s. Certificates expire after 5 years with no renewal.
  - Fix:
    - TLS 1.3.
    - A rotating discovery ID.
    - Automatic certificate renewal that peers accept through the old key.

## Part B: before any online (outside the home network) access

- [ ] **0.17 No open ports on the router.**
  - PCs and phones connect outward to a Lantoss relay or admin server, so nothing at home is exposed to the internet.
  - The LAN checks stay as they are.
  - Port forwarding is never offered.
- [ ] **0.18 End-to-end encryption through the relay.**
  - Files and text are encrypted with the devices' own keys, so the relay passes data it cannot read.
  - A stolen or hacked server then leaks no files.
- [ ] **0.19 Accounts and sign-in.**
  - Passkeys, or a password plus two-factor authentication.
  - A key for each device, with "sign out / remove this device".
  - Session expiry.
  - Per-account rate limits that don't let an attacker lock out real users.
  - An alert for each new sign-in or device.
- [ ] **0.20 Real HTTPS on the online address.**
  - A public certificate on the relay domain, TLS 1.3 only.
  - HSTS, `Secure` and `__Host-` cookies.
  - Per-account limits rather than per-IP, because many users share one IP online.
- [ ] **0.21 Admin server hardening.**
  - Roles for Admin, Server owner and Member. Admin actions need two-factor authentication.
  - The audit log is tamper-evident (hash-chained).
  - Reports from user servers are signed by the server's key, so they can't be forged.
- [ ] **0.22 Abuse limits.**
  - Storage quota, file-size and rate limits per account.
  - Optional malware scan of received files on Windows (Defender).
- [ ] **0.23 Signed automatic updates.**
  - The app checks for and installs updates whose signature it verifies, so security fixes reach every install quickly.
- [ ] **0.24 Monitoring and recovery.**
  - A security event log covering sign-ins, new devices, blocks and failures.
  - Key rotation and server backups.
  - A written incident plan.
- [ ] **0.25 Outside testing before launch.**
  - A written threat model.
  - Fuzz tests for the wire, mDNS and upload parsers.
  - A professional penetration test of the relay and apps before opening it to the public.

## Part C: releases and build

- [ ] **0.26 Signed releases.**
  - Today, the MSI and `Lantoss.exe` are unsigned.
  - On macOS, `install.sh` strips quarantine and re-signs whatever bundle is next to it (`packaging/macos/install.sh:49-51`), which throws away the build-time seal.
  - Fix:
    - Authenticode-sign the exe and MSI with a timestamp.
    - Use an Apple Developer ID with notarization.
    - Publish signed checksums.
    - Interim: `install.sh` verifies the shipped seal (`codesign --verify --strict`) instead of using `--force` to re-sign.
- [ ] **0.27 Explorer-menu certificate.**
  - Today, "CN=Lantoss Development" is trusted machine-wide and stays trusted after uninstall.
  - Its PFX has an empty password, passed with `/p` on the command line and never timestamped (`UI/ExplorerPackage.cs:27-57`, `Explorer/New-DevelopmentCertificate.ps1`).
  - Fix:
    - Remove that trust on uninstall.
    - Password-protect the PFX, passed in an environment variable.
    - Timestamp the signature.
    - Later, a real certificate.
- [ ] **0.28 Admin prompts from a user-writable folder.**
  - Today, the elevation and firewall steps run `Lantoss.exe` from `%LOCALAPPDATA%\Programs`. Other software running as the same user could replace that file.
  - Fix: signed binaries, or a signed elevated helper or MSI action.
- [ ] **0.29 Supply chain.**
  - `packages.lock.json` with locked restore.
  - A NuGet vulnerability audit that fails the build.
  - Re-verify cached build tools after extraction (`build-installer.ps1:40`, `common.ps1:57`).
  - Stack-protector and Control Flow Guard (CFG) flags for the Explorer DLL.
- [ ] **0.30 Security tests.** Each fix above gets a `Check(...)` test in a new `SecurityTests.cs`.

## Not verified

- These were found by reading the code, not by running attacks.
- Unconfirmed points, marked "unverified" by the reviewers:
  - Whether Windows sends sign-in hashes for LAN links.
  - The size of the WPF picture-decode memory spike.
  - Reuse of the extracted native libraries in %TEMP%.
  - Linux home-folder permissions on each distribution.
  - Current CVE status of BouncyCastle 2.7.0, QRCoder 1.8.1 and Avalonia 12.1.4. No advisories are known, but `dotnet list package --vulnerable` was not run.

## Decisions needed

1. **Stricter defaults (0.1, 0.2).** New PCs and phones need approval once on this PC. Existing paired PCs keep working. Recommended: yes.
2. **Phone page HTTPS on the home network (0.3).**
   - (a) HTTPS with the PC's own certificate. Each phone sees a browser warning once.
   - (b) Keep HTTP at home with the stricter approvals, and use real HTTPS only for online access.
   - Recommended: (a), as an option that is on by default.
3. **Online design (0.17).** An outbound relay or admin server (recommended), not router port forwarding.
4. **Admin and encryption (0.18 vs feature plan decision 1).**
   - If the admin must get copies of files, the relay cannot be fully end-to-end encrypted.
   - Recommended: end-to-end encrypted files, with the admin seeing only the details (who, what, when, size, hash).
5. **Signing costs (0.26).**
   - A Windows code-signing certificate, or Azure Trusted Signing (about $10 a month).
   - An Apple Developer account ($99 a year).
6. **Order.**
   - Recommended: Part A as 2.0.2, then the trusted-devices features (which build on 0.2), then Part B together with the admin server, with Part C alongside.
   - The feature plan's 0.13 security review is covered by this file.
