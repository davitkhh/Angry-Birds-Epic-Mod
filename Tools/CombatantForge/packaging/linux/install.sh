#!/bin/sh
# Installs Combatant Forge for this user. Needs no root.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
SHARE="${XDG_DATA_HOME:-${HOME:?}/.local/share}"
TARGET="$SHARE/CombatantForge"
BIN="$HOME/.local/bin"

if ! command -v python3 >/dev/null 2>&1; then
    echo "Combatant Forge needs Python 3: sudo apt install python3"
    exit 1
fi
if ! python3 -c "import PIL" 2>/dev/null; then
    echo "Combatant Forge needs Pillow: sudo apt install python3-pil"
    exit 1
fi

rm -rf "$TARGET"
mkdir -p "$TARGET" "$BIN" "$SHARE/applications" "$SHARE/icons/hicolor/192x192/apps"
cp -R "$HERE/app/." "$TARGET/"
cp "$HERE/uninstall.sh" "$TARGET/uninstall.sh"
cp "$HERE/combatant-forge.png" "$SHARE/icons/hicolor/192x192/apps/combatant-forge.png"

cat > "$BIN/combatant-forge" <<EOF
#!/bin/sh
exec python3 "$TARGET/desktop.py" "\$@"
EOF
chmod +x "$BIN/combatant-forge" "$TARGET/uninstall.sh"

cat > "$SHARE/applications/combatant-forge.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Combatant Forge
Comment=Build combatants and upload them into the game
Exec="$BIN/combatant-forge"
Icon=combatant-forge
Terminal=false
Categories=Development;Game;
EOF

echo "Installed. Open Combatant Forge from your app menu, or run: $BIN/combatant-forge"
if ! python3 -c "
import gi
gi.require_version('Gtk', '3.0')
gi.require_version('WebKit2', '4.1')
from gi.repository import Gtk, WebKit2" 2>/dev/null; then
    echo "It will open in your browser. For its own window: sudo apt install python3-gi gir1.2-webkit2-4.1"
fi
