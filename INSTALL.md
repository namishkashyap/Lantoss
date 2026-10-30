# Installing Lantoss 2.0

Lantoss runs on Windows, macOS and Linux. Phones need no app, because they use
the browser page. Install Lantoss on every computer you want to share between,
and keep them on the same trusted home Wi-Fi or Ethernet.

Get the files from the **[v2.0.2 release](https://github.com/namishkashyap/Lantoss/releases/tag/v2.0.2)**.
The previous Windows-only version stays available as [v1.9.0](https://github.com/namishkashyap/Lantoss/releases/tag/v1.9.0).

| Your computer | Download |
|---|---|
| Windows 10/11 (64-bit) | `Lantoss-Setup-2.0.2-x64.msi` |
| Mac with Apple Silicon (M1 or later) | `Lantoss-2.0.2-macOS-AppleSilicon.zip` |
| Mac with an Intel processor | `Lantoss-2.0.2-macOS-Intel.zip` |
| Ubuntu, Debian, Linux Mint, Pop!_OS (64-bit PC) | `lantoss_2.0.2_amd64.deb` |
| Raspberry Pi OS or other Debian-based ARM64 | `lantoss_2.0.2_arm64.deb` |
| Any other Linux (Fedora, Arch, openSUSE…) | `lantoss-2.0.2-linux-x64.tar.gz` or `lantoss-2.0.2-linux-arm64.tar.gz` |

These are development builds. They are not signed by Microsoft or notarized by
Apple, so each system asks you to confirm the first launch, as described below.

---

## Windows

### Install
1. If an older Lantoss is running, right-click its tray icon and choose **Exit Lantoss**.
2. Double-click `Lantoss-Setup-2.0.2-x64.msi`. If SmartScreen appears, choose **More info → Run anyway**.
3. Follow the setup. In **Custom Setup** you can add a desktop shortcut and **Launch Lantoss at sign-in**.
   The **Explorer sharing** feature adds *Share with Lantoss* to the right-click menu. On Windows 11
   it asks once to trust the "Lantoss Development" publisher, then asks for administrator approval.
4. Start **Lantoss** from the Start menu.

### First run
- Approve the Windows administrator prompt if Lantoss offers to set up **Private-network** firewall access.
- Make sure your home network is set to **Private** (Settings → Network & internet → your network).

### Update or uninstall
- Installing a newer MSI upgrades in place.
- Uninstall from **Settings → Apps → Installed apps → Lantoss**. Received files, history and settings are kept.

---

## macOS

Requires macOS 14 Sonoma or later.

### Install
1. Download the zip for your Mac (Apple menu → **About This Mac** shows *Chip: Apple M…* or *Processor: Intel*).
2. Double-click the zip to unpack it. You get `Lantoss.app`, **Install Lantoss.command** and `install.sh`.
   The app is signed for local use (ad-hoc), not by an Apple Developer ID. macOS therefore asks once before
   opening it, whether you use the installer or open `Lantoss.app` directly. Each time, the answer is **Open Anyway**.
3. Double-click **Install Lantoss.command** (recommended).
   - If macOS says it can't verify the file, click **Done**. Then open System Settings → **Privacy & Security**,
     scroll down, click **Open Anyway** next to "Install Lantoss.command", and confirm.
   - A Terminal window then:
     - copies Lantoss to **Applications**
     - removes the download quarantine flag
     - signs the app for this Mac (ad-hoc), because Apple Silicon Macs refuse to run unsigned code

     It may ask for your password.
   - Terminal users can instead run `sh install.sh` in the unpacked folder.

   **Saw "Lantoss is damaged and can't be opened" with the 2.0.0 download?** That build wasn't sealed.
   Click **Cancel** (not Move to Bin), download 2.0.2, and do step 3.
   Or run this in Terminal, adjusting the path to where you unpacked it:
   `xattr -cr ~/Downloads/Lantoss-2.0.2-macOS-AppleSilicon/Lantoss.app`.

   <details><summary>Prefer to do it by hand?</summary>

   ```sh
   cp -R Lantoss.app /Applications/
   xattr -dr com.apple.quarantine /Applications/Lantoss.app
   codesign --verify --strict --deep /Applications/Lantoss.app
   ```
   Only if that check fails, and you trust the download, re-sign it for this Mac with
   `codesign --force --deep --sign - /Applications/Lantoss.app`. Otherwise delete it and download again.
   </details>
4. Open **Lantoss** from Applications or Launchpad.

### First run
- Allow **incoming network connections** when macOS asks.
- Allow **Local Network** access (macOS 15+). If you declined, turn it on in
  System Settings → Privacy & Security → Local Network → Lantoss.
- If macOS says the app "can't be opened", open System Settings → Privacy & Security and choose **Open Anyway**.

### Uninstall
Quit Lantoss (Lantoss menu → Quit, or the tray menu → **Exit Lantoss**), then move
`/Applications/Lantoss.app` to the Trash. To also remove history and the device key:
`rm -rf ~/Library/Application\ Support/Lantoss`.

---

## Linux

Lantoss needs a desktop session (X11 or Wayland with XWayland).

### Debian, Ubuntu and derivatives (.deb)
```sh
sudo apt install ./lantoss_2.0.2_amd64.deb      # or lantoss_2.0.2_arm64.deb
```
Then start **Lantoss** from your applications menu, or run `lantoss`.
Uninstall with `sudo apt remove lantoss`.

### Any distribution (.tar.gz)
```sh
tar -xzf lantoss-2.0.2-linux-x64.tar.gz
cd lantoss-2.0.2
./install.sh            # installs for your user: ~/.local/opt/lantoss and a menu entry
# sudo ./install.sh     # or system-wide into /opt/lantoss
```
Uninstall with `./uninstall.sh` (or `sudo ./uninstall.sh` for a system-wide install).

### First run
- **Firewall.** If a firewall such as `ufw` or `firewalld` is on, allow Lantoss on your home network:
  ```sh
  sudo ufw allow from 192.168.0.0/16 to any port 48585:48588 proto tcp
  sudo ufw allow from 192.168.0.0/16 to any port 5353,48587 proto udp
  ```
- **Phone page ports.** Regular users cannot open ports 80 and 443, so the encrypted phone page uses port
  **48588** and port **48586** only redirects to it. Scan the QR code in **Phone & browser** rather than typing `share-wollvo.local`.
- **Notifications** use `notify-send`, which most desktops include (package `libnotify-bin`).
- **Tray icon** needs a desktop with status-icon support (GNOME needs the AppIndicator extension).
  Without one, closing the window minimizes Lantoss instead.

---

## All platforms

- Data and history live in your user profile: `%LOCALAPPDATA%\Lantoss` on Windows,
  `~/Library/Application Support/Lantoss` on macOS, `~/.local/share/Lantoss` on Linux.
  Received files go to `Downloads/Lantoss Inbox` unless you choose another folder in Settings.
- Open **Lantoss** on both computers and click the other one's name in **Devices** → **Connect**.
  The first time, both screens show the same code; approve on both if they match. Every transfer
  still asks the receiver to accept. New pairings need 2.0.2 or later on both computers.
- Phones: open **Phone & browser** and scan the QR code with the phone's camera. The QR code uses the
  computer's IP address, so it works on every phone, Android included.
  - **One-time certificate warning.** The page is encrypted with the computer's own certificate, so the
    phone's browser warns once ("Not private" / "Not secure"). Choose **Show details → Visit this website**
    (iPhone) or **Advanced → Proceed** (Android). To be sure it is your computer, compare the certificate code
    shown in **Phone & browser** with the browser's certificate details.
  - **Allow the phone.** A new phone waits until you click **Allow** in **Phone & browser** or **Devices**
    on the computer (or until it enters the access code, if you turned codes on).
  - To send files to one phone, choose it in **Send** under **Phones and browsers**.
- **Phone page address.** iPhones, iPads and computers can also type a name such as
  `http://share-wollvo.local`. Choose your own name in **Settings → Network → Phone page address**,
  for example `family-share`, which gives `http://family-share.local`. Many Android phones can't open `.local`
  names, so on Android use the QR code. If port 80 is unavailable, the name includes a port, for example
  `http://family-share.local:48586`.
- Lantoss never changes your router, DNS or hosts file, and works without internet or an account.
