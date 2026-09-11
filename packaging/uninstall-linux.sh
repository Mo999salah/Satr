#!/usr/bin/env bash
# Removes a per-user install created by install-linux.sh. Workspace data is kept.
set -euo pipefail
target="${XDG_DATA_HOME:-$HOME/.local/share}/satr"
applications="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
rm -rf -- "$target"
rm -f -- "$HOME/.local/bin/satr" "$HOME/.local/bin/satr-preview"
rm -f -- "$applications/satr.desktop" "$applications/satr-workspace.desktop" "$applications/satr-preview.desktop"
printf 'Uninstalled %s (workspace data preserved)\n' "$target"
