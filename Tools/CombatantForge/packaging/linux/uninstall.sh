#!/bin/sh
# Removes the app. Your combatants stay in the game project, under Tools/CombatantForge/projects.
SHARE="${XDG_DATA_HOME:-${HOME:?}/.local/share}"
rm -rf "$SHARE/CombatantForge"
rm -f "$HOME/.local/bin/combatant-forge" "$SHARE/applications/combatant-forge.desktop" "$SHARE/icons/hicolor/192x192/apps/combatant-forge.png"
echo "Combatant Forge removed."
