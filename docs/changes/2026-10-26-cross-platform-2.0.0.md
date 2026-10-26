# 2026-10-26: Lantoss 2.0.0 for Windows, macOS and Linux

This change continues on `release/2.0.0`. PR #2 was merged by the owner and PR #3 synced `main` back into the branch.
Only the items below are in scope.

## Items

- [x] 1. **Windows MSI 2.0.0.**
  - `installer/build-installer.ps1 -SkipPublish` builds `Lantoss-Setup-2.0.0-x64.msi` (84,172,800 bytes) from the published 2.0.0.0 app. It reuses the unchanged, already-signed Explorer DLL and identity package (1.9.0.0).
  - `installer/verify-installer.ps1`: version 2.0.0, current user only, 4 features, 19 files, 3 shortcuts. Passed.
- [x] 2. **New cross-platform project.** `source/Desktop` (Avalonia 12.1.4) reuses `Lantoss.Core` and the networking engine unchanged.
  - All ten pages, plus approval, pairing, comparison, manual-connection and sync dialogs.
  - Light/Dark/System appearance using the shared palette, and drag and drop onto the window.
  - Tray menu, OS notifications (`osascript` / `notify-send`, with text passed as arguments), network recovery and Cmd+Q / log-out shutdown.
  - Build with `build.ps1 -Mode Desktop`.
- [x] 3. **Portable identity.** `Identity.Portable` stores the device key in `identity.p12` (mode 600) in the app data folder. macOS uses its default key import, because it cannot use ephemeral keys.
- [x] 4. **macOS app.**
  - `packaging/build-macos.ps1` builds `Lantoss.app` for arm64 and x64.
  - Info.plist: LSMinimumSystemVersion 14.0, local-network usage text, Bonjour `_flowshare._tcp`.
  - Each zip contains `install.sh`, which copies the app, clears quarantine and ad-hoc signs it.
- [x] 5. **Linux app.**
  - `packaging/build-linux.ps1` builds x64 and arm64 `.deb` packages: `/opt/lantoss`, the `/usr/bin/lantoss` link, a menu entry, hicolor icons, and Depends for the bundled runtime and X11.
  - It also builds `.tar.gz` archives with per-user or system `install.sh` / `uninstall.sh`.
- [x] 6. **Install guide.** `INSTALL.md` has Windows, macOS and Linux sections: install, first run, firewall, uninstall and data locations.
- [x] 7. **Old and new on GitHub.**
  - `release/1.x` was created from `v1.9.0`.
  - The README has a Versions and downloads table (2.0.0 current, 1.9.0 previous).
  - The v2.0.0 release carries all installers and `SHA256SUMS.txt`.
- [x] 8. **Verify.** Details below.

## Verification

- `build.ps1 -Mode Test`: 245 checks passed, 1 skipped (the existing symbolic-link privilege skip). `-Mode CheckFormat`: passed. `-Mode Desktop`: built.
- `Lantoss.exe --render`: 20 Light/Dark page renders of the new app on Windows, reviewed.
- Packaging self-checks, all passing:
  - Executables and scripts are mode 755, and the `.deb` symlink and control fields are correct.
  - Info.plist is valid, and no Mach-O binary needs more than macOS 12.0.

| File | SHA-256 |
|---|---|
| Lantoss-Setup-2.0.0-x64.msi | 34abc3ddc8baceebc62c0e242ac3a23eceb8c4659653fb5ecb5ff79bb8238143 |
| Lantoss-2.0.0-macOS-AppleSilicon.zip | f960d3227933af7136e2ccd7819c77c4e38e1bc8ffd25d1c608b89e1b9bc4054 |
| Lantoss-2.0.0-macOS-Intel.zip | 057b4a827156a535e73d65e7350d78a820523942612f97b314a17733553604b8 |
| lantoss_2.0.0_amd64.deb | df154a73c4b5e13d8566087c5fbeef050b635c03c50873d54f61747d88bacdc1 |
| lantoss_2.0.0_arm64.deb | 36100dd0d0f3e77d216dd1933ce3b61821c9b460ecb6e5802f759c5fd9a45240 |
| lantoss-2.0.0-linux-x64.tar.gz | 074588358038a34e96f1dec68ad0ad199cfd345b64c69ed85a922711615efa80 |
| lantoss-2.0.0-linux-arm64.tar.gz | d7b196b63327bed6b45e9f40418c600a0ab59ae61e904e4a59982f5862179458 |

## Not verified

- The macOS and Linux builds were compiled and packaged but **not launched**: there is no Mac or Linux/WSL here. Specifically untested:
  - Gatekeeper with ad-hoc signing
  - local-network permission
  - binding UDP 5353 next to the system mDNS responder
  - tray icons on each desktop
  - `dpkg`/`lintian` checks
  - `Identity.Portable`, which only runs on macOS/Linux
- All installers are unsigned development builds. There is no Apple Developer ID or notarization.

## Out of scope

- Apple notarization, apt repository hosting, and Finder or file-manager right-click menus.
