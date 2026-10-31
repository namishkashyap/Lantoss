#!/bin/sh
# Installs hopo from this folder.
#   ./install.sh        for the current user: ~/.local/opt/hopo, ~/.local/bin/hopo and a menu entry
#   sudo ./install.sh   for all users: /opt/hopo, /usr/local/bin/hopo and a menu entry
# Running it again upgrades in place. Remove hopo with uninstall.sh (it is also copied into the install folder).
set -eu

APP_ARCH="@ARCH@"
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

case "$(uname -m)" in
    x86_64|amd64) machine=x86_64 ;;
    aarch64|arm64) machine=aarch64 ;;
    *) machine=$(uname -m) ;;
esac
if [ "$machine" != "$APP_ARCH" ]; then
    echo "This package is for $APP_ARCH, but this computer is $machine. Download the matching hopo package." >&2
    exit 1
fi
if [ ! -f "$here/app/hopo" ]; then
    echo "app/hopo was not found next to install.sh. Extract the whole archive first." >&2
    exit 1
fi

if [ "$(id -u)" -eq 0 ]; then
    if command -v dpkg-query >/dev/null 2>&1 && dpkg-query -W -f='${Status}' hopo 2>/dev/null | grep -q 'ok installed'; then
        echo "The hopo .deb package is installed. Upgrade it with the .deb, or remove it first: sudo apt remove hopo" >&2
        exit 1
    fi
    prefix=/opt/hopo
    bindir=/usr/local/bin
    datadir=/usr/local/share
else
    prefix="$HOME/.local/opt/hopo"
    bindir="$HOME/.local/bin"
    datadir="${XDG_DATA_HOME:-$HOME/.local/share}"
fi
appsdir="$datadir/applications"
icondir="$datadir/icons/hicolor"

echo "Installing hopo to $prefix"
mkdir -p "$(dirname "$prefix")"
rm -rf "$prefix.new"
cp -R "$here/app" "$prefix.new"
chmod 755 "$prefix.new/hopo"
if [ -f "$prefix.new/createdump" ]; then chmod 755 "$prefix.new/createdump"; fi
cp "$here/uninstall.sh" "$prefix.new/uninstall.sh"
chmod 755 "$prefix.new/uninstall.sh"
rm -rf "$prefix"
mv "$prefix.new" "$prefix"

mkdir -p "$bindir"
ln -sfn "$prefix/hopo" "$bindir/hopo"

mkdir -p "$icondir/256x256/apps" "$appsdir"
cp "$here/hopo.png" "$icondir/256x256/apps/hopo.png"
chmod 644 "$icondir/256x256/apps/hopo.png"

# Point the menu entry at the installed executable, escaped as the Desktop Entry spec requires.
exec_path=$(printf '%s' "$prefix/hopo" | sed -e 's/[\\"`$]/\\\\&/g' -e 's/%/%%/g')
while IFS= read -r line || [ -n "$line" ]; do
    case "$line" in
        Exec=*) printf 'Exec="%s"\n' "$exec_path" ;;
        *) printf '%s\n' "$line" ;;
    esac
done < "$here/hopo.desktop" > "$appsdir/hopo.desktop"
chmod 644 "$appsdir/hopo.desktop"

# Refresh menu and icon caches where the tools exist. Failures are harmless.
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q "$appsdir" >/dev/null 2>&1 || true; fi
touch "$icondir" 2>/dev/null || true
if [ -f "$icondir/icon-theme.cache" ] && command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f "$icondir" >/dev/null 2>&1 || true
fi

# Earlier versions were called Lantoss. Remove that copy so only hopo is on the menu; settings stay in the Lantoss data folder.
old="$(dirname "$prefix")/lantoss"
if [ -d "$old" ] && [ -f "$old/Lantoss" ]; then rm -rf "$old"; fi
if [ -L "$bindir/lantoss" ]; then rm -f "$bindir/lantoss"; fi
rm -f "$appsdir/lantoss.desktop" "$icondir/256x256/apps/lantoss.png"
echo "hopo is installed. Start it from your applications menu or run: hopo"
case ":$PATH:" in
    *":$bindir:"*) ;;
    *) echo "Note: $bindir is not on your PATH, so run $prefix/hopo or use the menu entry." ;;
esac
if [ "$(id -u)" -eq 0 ]; then
    echo "To uninstall: sudo $prefix/uninstall.sh"
else
    echo "To uninstall: $prefix/uninstall.sh"
fi
