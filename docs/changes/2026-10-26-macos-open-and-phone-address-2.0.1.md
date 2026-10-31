# 2026-10-26: Lantoss 2.0.1, macOS opening and phone address

Reported problems:
- On macOS, the downloaded app shows "Lantoss is damaged and can't be opened".
- The custom address (`share-wollvo.local`) does not open on the phone.

Requested: let the admin choose the address. Only the items below are in scope.

## Items

- [x] 1. **macOS app opens instead of being reported as damaged.**
  - [x] The macOS app is published as one single-file executable plus its three native libraries. `Contents/MacOS` now holds only Mach-O code (4 files, previously 300+), which a sealed signature requires.
  - [x] The zip includes a double-clickable **Install Lantoss.command**. It is mode 755 and has the same script as `install.sh`: it clears quarantine and ad-hoc signs the app on the Mac. This turns the "damaged" dead end into macOS's normal "Open Anyway" flow.
  - [x] INSTALL.md explains the steps, including what to do after seeing "damaged".
  - [x] The bundle is sealed and ad-hoc signed while packaging, as the owner approved, using `rcodesign` 0.29.0. The download is pinned to SHA-256 `54bb500e…159aaa`, which matches the publisher's checksum file.
    - All four Mach-O files are signed, and `Contents/_CodeSignature/CodeResources` lists the three libraries with their cdhash and requirement.
    - The build checks that the main executable's ad-hoc signature binds the exact Info.plist (slot 1) and CodeResources (slot 3) hashes. `rcodesign verify` is not used, because it misreports ad-hoc signatures.
    - The macOS assets of release v2.0.1 were replaced with the sealed builds.
- [x] 2. **Phone address works on every phone.**
  - The QR code and "Copy link" use the IP address by default, in both the Windows and macOS/Linux apps.
  - The name is shown as "you can also type …", with a toggle for a name QR code.
  - Local names are published on any port, and the link includes the port when it is not 80.
- [x] 3. **Custom address chosen by the admin.**
  - Settings → Network → **Phone page address** in both apps, stored as `Preferences.LocalName`.
  - The name is validated as one lowercase DNS label of 1 to 40 characters and normalized (`Family-Share.local` → `family-share`).
  - It is applied immediately, because the name service restarts when the name or port changes.
- [x] 4. **Release 2.0.1.**
  - Version 2.0.1 throughout, with a CHANGELOG entry.
  - All seven installers rebuilt and verified, and release v2.0.1 published.

## Verification

- `build.ps1 -Mode Test`: 250 checks passed, 1 skipped. The 5 new checks cover:
  - address validation and normalization
  - saving the address and refusing invalid ones
  - claiming a custom alias
  - the real UDP responder publishing `http://family-share.local:48586`
  - device links that include a port
- One existing test was updated, because it required device links without a port.
- `-Mode CheckFormat` passed, and both apps build.
- The MSI was built from a staging folder, because the running `app\Lantoss.exe` was in use. `verify-installer.ps1`: 2.0.1, current user only, 19 files, 3 shortcuts. Passed.
- macOS and Linux packaging self-checks passed for all six packages.

| File | SHA-256 |
|---|---|
| Lantoss-Setup-2.0.1-x64.msi | ee844acc3fafbb8b32a0924deedb0dbe307d97cb9a424603ac58304206c8b381 |
| Lantoss-2.0.1-macOS-AppleSilicon.zip | 5d847edecd253e4fa23a6520518a3fc1a32402b3ddd532528b448e88ad977544 |
| Lantoss-2.0.1-macOS-Intel.zip | 0cc6caad61de441dee300b9ab975a42bc65c30e3e42c3e6e971713e2fa4a077e |
| lantoss_2.0.1_amd64.deb | 3c00277e6ceed69a6d142252821c1965686db9e03a9549b2b3bc67cb9ba0ae4c |
| lantoss_2.0.1_arm64.deb | 40895fa660cae40bb732a279c45b78e35e19a481904514cc446f3850184615b8 |
| lantoss-2.0.1-linux-x64.tar.gz | df238ec7a59dead20c288d8f0e4b85617512ceb0f37028481292a9fac128c94e |
| lantoss-2.0.1-linux-arm64.tar.gz | 6caa944794f2bb70c1941cb682bb43cdf1dfbac77ae78cf5518abbc16dc630a7 |

## Not verified

- A live `.local` lookup from this PC was not run, because a temporary test program would trigger a Windows Firewall prompt. There was also no test on a real phone or Mac.

## Out of scope

- An Apple Developer ID signature and notarization, which need a paid Apple account.
- Making Android resolve `.local` names. The QR code uses the IP address instead.
