#!/bin/sh
# Removes a Lantoss installed with install.sh. Your settings and received files are kept.
#   ./uninstall.sh        removes the per-user install
#   sudo ./uninstall.sh   removes the system-wide install
set -eu

if [ "$(id -u)" -eq 0 ]; then
    if command -v dpkg-query >/dev/null 2>&1 && dpkg-query -W -f='${Status}' lantoss 2>/dev/null | grep -q 'ok installed'; then
        echo "Lantoss was installed from the .deb package. Remove it with: sudo apt remove lantoss" >&2
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

if [ -L "$bindir/lantoss" ]; then
    case "$(readlink "$bindir/lantoss")" in
        "$prefix"/*) rm -f "$bindir/lantoss" ;;
    esac
fi
rm -f "$appsdir/lantoss.desktop" "$icondir/256x256/apps/lantoss.png"
rm -rf "$prefix" "$prefix.new"

if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q "$appsdir" >/dev/null 2>&1 || true; fi
touch "$icondir" 2>/dev/null || true
if [ -f "$icondir/icon-theme.cache" ] && command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f "$icondir" >/dev/null 2>&1 || true
fi

echo "Lantoss has been removed from $prefix."
