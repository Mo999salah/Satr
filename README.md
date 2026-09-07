# Satr

**An English-first terminal workspace for Windows and Linux.**

[Downloads](https://github.com/Mo999salah/Satr/releases) · [Issues](https://github.com/Mo999salah/Satr/issues) · [Contributing](CONTRIBUTING.md) · [MIT license](LICENSE)

Satr provides a full-width terminal with an English interface, project tabs, and direct access to shells and AI CLIs on Windows and Linux. The separate prompt editor has been removed from the current source; existing preview downloads may still include it. The application interface is in English.

Built by [Mohamad Salah](https://mohamadsala.me/).

> **0.3.0:** full-width terminal, English UI, search, command palette, tab controls, persisted window settings, scrollback reflow, IME plumbing and optional native Wayland startup.

## Downloads

| Platform | Package |
| --- | --- |
| Windows x64 | Release installer and portable archive |
| Linux x64 | Release portable archive |
| Source | GitHub source archive |

Packages include the .NET runtime. Install AI tools separately and make them available on `PATH`. The Windows installer is unsigned. No AUR package is published.

## Features implemented

- Full-width terminal with project-labelled tabs; type or paste directly into the active CLI.
- ANSI rendering with Unicode bidirectional layout and Arabic shaping.
- Project directories, multiple sessions and lazy restoration of saved tabs.
- Search across scrollback with `Ctrl+Shift+F`, `Enter`, `Shift+Enter` and `F3`.
- Custom tab names, tab movement, ended-session restart and per-tab status.
- Font family, font size, scrollback depth and window geometry persistence.
- Current-directory tracking through shell OSC 7 integration when the shell emits it.
- Arabic IME preedit support on the terminal surface.
- Launch actions for Codex, `codex resume`, Claude Code, Agy, Omp (plus `--continue` resume) and the system shell.
- Bracketed paste, application cursor keys, alternate screen, SGR mouse and focus reporting.
- Selection, clipboard text, scrollback, font-size adjustment and a readable text transcript.
- Atomic workspace saves, backup recovery and draft archiving when closing tabs.
- Shared Avalonia UI with Windows ConPTY and Linux PTY connections through Porta.Pty.

Implemented launch actions do not mean verified compatibility with every CLI version. Satr does not bundle an AI model or subscription.

## Installation

### Windows

Run the installer, or extract the **entire** portable ZIP and open `Satr.exe`. Keep native DLLs and ConPTY host directories beside the executable. The installer installs per user as **Satr Preview**, separately from the earlier WPF version. ConPTY requires Windows 10 build 17763 or newer.

### Arch Linux

The portable package targets x86_64. X11/XWayland remains the default. Native Wayland is available as an explicit experimental opt-in:

```bash
sudo pacman -S --needed libx11 libice libsm libxrandr libxi libxcursor fontconfig freetype2 icu openssl zlib ttf-dejavu noto-fonts noto-fonts-emoji xorg-xwayland
mkdir satr
tar -xzf Satr-0.3.0-linux-x64.tar.gz -C satr
cd satr
chmod +x Satr
./Satr
# Optional native Wayland backend on Avalonia 12.1:
SATR_BACKEND=wayland ./Satr
# Optional per-user installation and application-menu entry:
bash install-linux.sh
```

The script installs into `$XDG_DATA_HOME/satr`, falling back to `~/.local/share/satr`, creates `~/.local/bin/satr` plus an application-menu entry, and removes legacy `satr-preview` launchers. `bash uninstall-linux.sh` (beside it) removes the install but keeps workspace data. Do not run it with `sudo`. Close an existing installation before replacing files.

## Workflow

1. Choose a project directory and open a shell or Codex session.
2. Type directly into the terminal or paste clipboard text with **Ctrl+V**.
3. Review the text and press **Enter** to submit it.

Multi-line pastes are rejected if the session has not enabled bracketed paste. The displayed directory follows shell OSC 7 when available and otherwise remains the starting directory. Restoration launches only the selected tab. Saved Codex tabs reopen through `codex resume`. Closing the window saves the workspace and terminates sessions.

| Shortcut | Action |
| --- | --- |
| Ctrl+Shift+T | Open a shell tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Switch tabs |
| Ctrl+Shift+W | Close the current tab |
| Ctrl+Shift+C | Copy selected terminal text |
| Ctrl+Shift+F | Search scrollback |
| F3 / Shift+F3 | Next / previous search result |
| Ctrl+Shift+R | Restart ended session |
| Ctrl+Shift+PageUp / PageDown | Move current tab |
| Ctrl+V in the terminal | Paste clipboard text |
| Shift + mouse selection | Select while a CLI captures mouse input |

## Data and privacy

Satr adds no telemetry, updater, API-key store or application network client. Launched CLIs manage their own authentication and network activity. Drafts are plain text; avoid retaining secrets in them.

| Platform | Workspace location |
| --- | --- |
| Windows | `%LocalAppData%\Satr` |
| Linux | `$XDG_STATE_HOME/Satr` or `~/.local/state/Satr` |

`SATR_DATA_DIR` overrides the location. A one-time automatic migration moves an existing `Satr-Preview` workspace to `Satr`. The old WPF workspace is not automatically imported or modified. Do not share a data directory between running versions. Clipboard images are stored under `clipboard-images` in the workspace and are not uploaded by Satr. Uninstalling on Windows preserves workspace data.

## Build from source

Requires **.NET SDK 10**. PowerShell 7 is needed for packaging; Inno Setup 6 is optional for the Windows installer.

```bash
git clone https://github.com/Mo999salah/Satr.git
cd Satr
dotnet build src/Satr/Satr.csproj -c Release
dotnet run --project src/Satr/Satr.csproj
```

```powershell
./scripts/Build.ps1 -Dotnet dotnet
# Optional Windows installer:
./scripts/Build.ps1 -Dotnet dotnet -Compiler 'C:/path/to/ISCC.exe'
```

Direct Linux publication:

```bash
dotnet publish src/Satr/Satr.csproj -c Release -r linux-x64 --self-contained true
```

Packages go to `artifacts/`, which is excluded from Git. GitHub Actions builds archives on pushes and pull requests; it does not automatically publish releases or run runtime checks.

Optional checks (run before release):

```bash
dotnet run --project tests/Satr.PortChecks/Satr.PortChecks.csproj
dotnet run --project tests/Satr.BufferTests/Satr.BufferTests.csproj
```

## Architecture

| Component | Responsibility |
| --- | --- |
| `TerminalBuffer.cs` | ANSI parsing, screen state, scrollback and negotiated modes |
| `SmartRtl.cs` | Unicode bidirectional ordering |
| `TerminalView.cs` | Cell layout, shaped text, selection and drawing |
| `PtySession.cs` | Startup, UTF-8 streams, input queue and PTY lifecycle |
| `MainWindow.cs` | Workspace UI and session interaction |
| `WorkspaceStore.cs` | Atomic persistence and recovery |

Dependencies: **Avalonia 12.1.2**, **Porta.Pty 2.2.2**, **Unicode.Bidi 0.3.18**. Satr uses the free Avalonia framework, not commercial XPF.

## Known limitations

- CLI acceptance of pasted image paths depends on the selected tool; Satr stores the image and sends its path.
- Current-directory tracking requires shell OSC 7 integration; shells that do not emit it retain the launch directory.
- Native Wayland is experimental and opt-in; X11/XWayland remains the fallback path.
- Clean-machine installer validation, binary signing and live Windows/Linux CLI compatibility remain release work.

## Contributing and license

Focused bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

Satr is open source under the [MIT license](LICENSE). It is an independently maintained derivative of [RtlTerminal](https://github.com/mirbehnam/RtlTerminal), not an engine written from scratch. Parser and rendering lineage remain attributed; the cross-platform project replaces WPF, Registry settings, Windows window helpers and the original session bridge.

Original copyright and dependency notices are preserved in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [licenses/](licenses/). Satr is not affiliated with OpenAI, Anthropic or the upstream project.
