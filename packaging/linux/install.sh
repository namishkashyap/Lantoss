#!/bin/sh
# Installs Lantoss from this folder.
#   ./install.sh        for the current user: ~/.local/opt/lantoss, ~/.local/bin/lantoss and a menu entry
#   sudo ./install.sh   for all users: /opt/lantoss, /usr/local/bin/lantoss and a menu entry
# Running it again upgrades in place. Remove Lantoss with uninstall.sh (it is also copied into the install folder).
set -eu

APP_ARCH="@ARCH@"
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

case "$(uname -m)" in
    x86_64|amd64) machine=x86_64 ;;
    aarch64|arm64) machine=aarch64 ;;
    *) machine=$(uname -m) ;;
esac
if [ "$machine" != "$APP_ARCH" ]; then
    echo "This package is for $APP_ARCH, but this computer is $machine. Download the matching Lantoss package." >&2
    exit 1
fi
if [ ! -f "$here/app/Lantoss" ]; then
    echo "app/Lantoss was not found next to install.sh. Extract the whole archive first." >&2
    exit 1
fi

if [ "$(id -u)" -eq 0 ]; then
    if command -v dpkg-query >/dev/null 2>&1 && dpkg-query -W -f='${Status}' lantoss 2>/dev/null | grep -q 'ok installed'; then
        echo "The Lantoss .deb package is installed. Upgrade it with the .deb, or remove it first: sudo apt remove lantoss" >&2
        exit 1
    fi
    prefix=/opt/lantoss
    bindir=/usr/local/bin
    datadir=/usr/local/share
else
    prefix="$HOME/.local/opt/lantoss"
    bindir="$HOME/.local/bin"
    datadir="${XDG_DATA_HOME:-$HOME/.local/share}"
fi
appsdir="$datadir/applications"
icondir="$datadir/icons/hicolor"

echo "Installing Lantoss to $prefix"
mkdir -p "$(dirname "$prefix")"
rm -rf "$prefix.new"
cp -R "$here/app" "$prefix.new"
chmod 755 "$prefix.new/Lantoss"
if [ -f "$prefix.new/createdump" ]; then chmod 755 "$prefix.new/createdump"; fi
cp "$here/uninstall.sh" "$prefix.new/uninstall.sh"
chmod 755 "$prefix.new/uninstall.sh"
rm -rf "$prefix"
mv "$prefix.new" "$prefix"

mkdir -p "$bindir"
ln -sfn "$prefix/Lantoss" "$bindir/lantoss"

mkdir -p "$icondir/256x256/apps" "$appsdir"
cp "$here/lantoss.png" "$icondir/256x256/apps/lantoss.png"
chmod 644 "$icondir/256x256/apps/lantoss.png"

# Point the menu entry at the installed executable, escaped as the Desktop Entry spec requires.
exec_path=$(printf '%s' "$prefix/Lantoss" | sed -e 's/[\\"`$]/\\\\&/g' -e 's/%/%%/g')
while IFS= read -r line || [ -n "$line" ]; do
    case "$line" in
        Exec=*) printf 'Exec="%s"\n' "$exec_path" ;;
        *) printf '%s\n' "$line" ;;
    esac
done < "$here/lantoss.desktop" > "$appsdir/lantoss.desktop"
chmod 644 "$appsdir/lantoss.desktop"

# Refresh menu and icon caches where the tools exist. Failures are harmless.
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q "$appsdir" >/dev/null 2>&1 || true; fi
touch "$icondir" 2>/dev/null || true
if [ -f "$icondir/icon-theme.cache" ] && command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f "$icondir" >/dev/null 2>&1 || true
fi

echo "Lantoss is installed. Start it from your applications menu or run: lantoss"
case ":$PATH:" in
    *":$bindir:"*) ;;
    *) echo "Note: $bindir is not on your PATH, so run $prefix/Lantoss or use the menu entry." ;;
esac
if [ "$(id -u)" -eq 0 ]; then
    echo "To uninstall: sudo $prefix/uninstall.sh"
else
    echo "To uninstall: $prefix/uninstall.sh"
fi
