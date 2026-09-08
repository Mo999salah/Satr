#!/usr/bin/env bash
set -euo pipefail

state_root="${XDG_STATE_HOME:-$HOME/.local/state}"
export SATR_DATA_DIR="$state_root/Satr-Dev"
launcher="$(readlink -f -- "${BASH_SOURCE[0]}")"
exec "$(dirname -- "$launcher")/Satr" "$@"
