#!/bin/sh
# Installs hopo.app into /Applications and makes macOS accept it.
# Run it from Terminal in the unzipped folder:  sh install.sh
#
# hopo is not signed with an Apple Developer ID, so this script:
#   1. copies hopo.app to /Applications,
#   2. removes the download quarantine flag,
#   3. checks the build-time ad hoc seal, and re-signs ad hoc on this Mac only if you agree after a
#      failed check (a broken seal means the app was modified or the download is damaged).
set -eu

APP_ARCH="@ARCH@"
here=$(cd "$(dirname "$0")" && pwd)
src="$here/hopo.app"
dest="/Applications/hopo.app"

if [ ! -d "$src" ]; then
    echo "hopo.app was not found next to install.sh. Unzip the whole download first." >&2
    exit 1
fi

apple_silicon=$(/usr/sbin/sysctl -n hw.optional.arm64 2>/dev/null || echo 0)
if [ "$APP_ARCH" = "arm64" ] && [ "$apple_silicon" != "1" ]; then
    echo "This download is for Apple Silicon Macs. Download the Intel version of hopo instead." >&2
    exit 1
fi
if [ "$APP_ARCH" = "x86_64" ] && [ "$apple_silicon" = "1" ]; then
    echo "Note: this is the Intel version. The Apple Silicon version runs better on this Mac."
    if ! /usr/bin/arch -x86_64 /usr/bin/true 2>/dev/null; then
        echo "The Intel version needs Rosetta. Install it with: softwareupdate --install-rosetta --agree-to-license" >&2
        exit 1
    fi
fi

if /usr/bin/pgrep -x hopo >/dev/null 2>&1 || /usr/bin/pgrep -x Lantoss >/dev/null 2>&1; then
    echo "hopo is running. Quit it, then run this script again." >&2
    exit 1
fi

run=""
if [ ! -w /Applications ] || { [ -e "$dest" ] && [ ! -w "$dest" ]; }; then
    echo "Your administrator password is needed to install into /Applications."
    run="sudo"
fi

if [ -d "/Applications/Lantoss.app" ]; then
    # Earlier versions were called Lantoss. Settings stay in the Lantoss data folder and carry over.
    echo "Removing the older Lantoss.app"
    $run /bin/rm -rf "/Applications/Lantoss.app"
fi
echo "Installing hopo to $dest"
$run /bin/rm -rf "$dest"
$run /usr/bin/ditto "$src" "$dest"
$run /bin/chmod 755 "$dest/Contents/MacOS/hopo"
$run /usr/bin/xattr -dr com.apple.quarantine "$dest" 2>/dev/null || true
echo "Checking the hopo seal"
if ! /usr/bin/codesign --verify --strict --deep "$dest" 2>/dev/null; then
    echo >&2
    echo "WARNING: the seal on hopo.app does not match its contents." >&2
    echo "The app may have been modified after it was built, or the download is damaged." >&2
    echo "The safe choice is to delete it and download hopo again." >&2
    answer=""
    if (: </dev/tty) 2>/dev/null; then
        printf 'Re-sign it for this Mac anyway? [y/N] ' >/dev/tty
        read -r answer </dev/tty || answer=""
    fi
    case "$answer" in
        y|Y) ;;
        *) echo "Not signed. hopo is in $dest but will not open until it is replaced or re-signed." >&2; exit 1 ;;
    esac
    echo "Signing hopo for this Mac (ad hoc)"
    $run /usr/bin/codesign --force --deep --sign - "$dest"
    /usr/bin/codesign --verify --strict --deep "$dest"
fi

echo
echo "hopo is installed. Open it from Launchpad or the Applications folder."
echo "When macOS asks whether hopo may find devices on your local network, choose Allow."
