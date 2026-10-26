#!/bin/sh
# Installs Lantoss.app into /Applications and makes macOS accept it.
# Run it from Terminal in the unzipped folder:  sh install.sh
#
# Lantoss is not signed with an Apple Developer ID, so this script:
#   1. copies Lantoss.app to /Applications,
#   2. removes the download quarantine flag,
#   3. signs the app ad hoc on this Mac, because Apple Silicon refuses to run unsigned arm64 code.
set -eu

APP_ARCH="@ARCH@"
here=$(cd "$(dirname "$0")" && pwd)
src="$here/Lantoss.app"
dest="/Applications/Lantoss.app"

if [ ! -d "$src" ]; then
    echo "Lantoss.app was not found next to install.sh. Unzip the whole download first." >&2
    exit 1
fi

apple_silicon=$(/usr/sbin/sysctl -n hw.optional.arm64 2>/dev/null || echo 0)
if [ "$APP_ARCH" = "arm64" ] && [ "$apple_silicon" != "1" ]; then
    echo "This download is for Apple Silicon Macs. Download the Intel version of Lantoss instead." >&2
    exit 1
fi
if [ "$APP_ARCH" = "x86_64" ] && [ "$apple_silicon" = "1" ]; then
    echo "Note: this is the Intel version. The Apple Silicon version runs better on this Mac."
    if ! /usr/bin/arch -x86_64 /usr/bin/true 2>/dev/null; then
        echo "The Intel version needs Rosetta. Install it with: softwareupdate --install-rosetta --agree-to-license" >&2
        exit 1
    fi
fi

if /usr/bin/pgrep -x Lantoss >/dev/null 2>&1; then
    echo "Lantoss is running. Quit it, then run this script again." >&2
    exit 1
fi

run=""
if [ ! -w /Applications ] || { [ -e "$dest" ] && [ ! -w "$dest" ]; }; then
    echo "Your administrator password is needed to install into /Applications."
    run="sudo"
fi

echo "Installing Lantoss to $dest"
$run /bin/rm -rf "$dest"
$run /usr/bin/ditto "$src" "$dest"
$run /bin/chmod 755 "$dest/Contents/MacOS/Lantoss"
$run /usr/bin/xattr -dr com.apple.quarantine "$dest" 2>/dev/null || true
echo "Signing Lantoss for this Mac (ad hoc)"
$run /usr/bin/codesign --force --deep --sign - "$dest"
/usr/bin/codesign --verify --deep "$dest"

echo
echo "Lantoss is installed. Open it from Launchpad or the Applications folder."
echo "When macOS asks whether Lantoss may find devices on your local network, choose Allow."
