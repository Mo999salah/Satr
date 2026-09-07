#!/usr/bin/env bash
set -euo pipefail
source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
target="${XDG_DATA_HOME:-$HOME/.local/share}/satr"
applications="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
if [[ "$source_dir" == "$target" ]]; then
    printf 'Already installed in %s\n' "$target"
    exit 0
fi
mkdir -p -- "$target" "$applications" "$HOME/.local/bin"
cp -a -- "$source_dir/." "$target/"
chmod +x -- "$target/Satr"
ln -sfn -- "$target/Satr" "$HOME/.local/bin/satr"
# Upgrade path from the preview name: drop legacy launcher entries.
rm -f -- "$HOME/.local/bin/satr-preview" "$applications/satr-preview.desktop"
# Desktop Exec has its own escaping rules (not shell quoting).
escaped=${target//\\/\\\\}
escaped=${escaped//\"/\\\"}
escaped=${escaped//\$/\\\$}
escaped=${escaped//\`/\\\`}
escaped=${escaped//%/%%}
printf '[Desktop Entry]\nType=Application\nName=Satr\nComment=English terminal workspace\nExec="%s/Satr"\nTerminal=false\nCategories=System;TerminalEmulator;\nStartupWMClass=Satr\n' "$escaped" > "$applications/satr.desktop"
printf 'Installed: %s\nRun: %s/Satr (or: satr)\n' "$target" "$target"
