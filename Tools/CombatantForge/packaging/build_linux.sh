#!/bin/sh
# Builds dist/CombatantForge-linux.tar.gz from this folder.
set -e
FORGE="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$FORGE/dist"
WORK="$(mktemp -d)"
trap 'rm -rf "${WORK:?}"' EXIT
PKG="$WORK/CombatantForge"
mkdir -p "$PKG/app/web" "$OUT"
cp "$FORGE"/forge.py "$FORGE"/desktop.py "$FORGE"/gamedata.py "$FORGE"/publish.py "$FORGE"/portable.py "$FORGE"/existing.py "$FORGE"/effects.py "$FORGE"/projectiles.py "$PKG/app/"
cp "$FORGE"/web/index.html "$FORGE"/web/app.js "$FORGE"/web/style.css "$FORGE"/web/projectiles.js "$PKG/app/web/"
cp "$FORGE"/packaging/linux/install.sh "$FORGE"/packaging/linux/uninstall.sh "$FORGE"/packaging/linux/README.txt "$PKG/"
chmod +x "$PKG/install.sh" "$PKG/uninstall.sh"
(cd "$FORGE" && python3 -B -c "
import gamedata as g
open('$PKG/combatant-forge.png', 'wb').write(g.rig_icon('MinionPig'))")
tar -czf "$OUT/CombatantForge-linux.tar.gz" -C "$WORK" CombatantForge
echo "$OUT/CombatantForge-linux.tar.gz"
