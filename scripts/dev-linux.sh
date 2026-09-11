#!/usr/bin/env bash
# Build, verify, install, and open an isolated Linux development copy of Satr.
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="dotnet"
launch=true

while (($#)); do
    case "$1" in
        --dotnet)
            dotnet_command="${2:?--dotnet requires a path}"; shift 2 ;;
        --no-launch)
            launch=false; shift ;;
        *)
            printf 'Usage: %s [--dotnet PATH] [--no-launch]\n' "${0##*/}" >&2; exit 64 ;;
    esac
done

if [[ "$dotnet_command" == */* ]]; then
    test -x "$dotnet_command" || { printf 'dotnet is not executable: %s\n' "$dotnet_command" >&2; exit 1; }
else
    command -v "$dotnet_command" >/dev/null || { printf 'dotnet was not found; pass --dotnet PATH\n' >&2; exit 1; }
fi

"$dotnet_command" run --project "$root/tests/Satr.BufferTests/Satr.BufferTests.csproj" -c Release --nologo
"$dotnet_command" run --project "$root/tests/Satr.PortChecks/Satr.PortChecks.csproj" -c Release --nologo

publish="$root/artifacts/linux-dev"
"$dotnet_command" publish "$root/src/Satr/Satr.csproj" -c Release -r linux-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o "$publish" --nologo

data_root="${XDG_DATA_HOME:-$HOME/.local/share}"
target="$data_root/satr-dev"
applications="$data_root/applications"
launcher="$HOME/.local/bin/satr-dev"
stage="$target.next-$$"

if pgrep -af "^$target/Satr( |$)" >/dev/null; then
    printf 'Close the running Satr Dev window before updating it.\n' >&2
    exit 1
fi

rm -rf -- "$stage"
mkdir -p -- "$stage" "$applications" "$(dirname -- "$launcher")"
cp -a -- "$publish/." "$stage/"
install -m 755 "$root/packaging/satr-dev-launcher.sh" "$stage/satr-dev"
chmod +x "$stage/Satr"

if [[ -d "$target" ]]; then
    previous="$target.previous"
    rm -rf -- "$previous"
    mv -- "$target" "$previous"
fi
mv -- "$stage" "$target"
ln -sfn -- "$target/satr-dev" "$launcher"
install -m 755 "$root/packaging/satr-dev.desktop" "$applications/satr-dev.desktop"
install -m 755 "$root/packaging/satr-dev-workspace.desktop" "$applications/satr-dev-workspace.desktop"

printf 'Installed Satr Dev: %s\nRun: satr-dev\nState: %s/Satr-Dev\n' "$target" "${XDG_STATE_HOME:-$HOME/.local/state}"
if "$launch"; then
    setsid -f "$launcher" >/tmp/satr-dev-launch.log 2>&1
fi
