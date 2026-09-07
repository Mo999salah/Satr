# Satr

**A project workspace terminal for AI coding CLIs — with correct Arabic and mixed-script rendering.**

[Downloads](https://github.com/Mo999salah/Satr/releases) · [Issues](https://github.com/Mo999salah/Satr/issues) · [Contributing](CONTRIBUTING.md) · [MIT license](LICENSE)

Satr is a desktop terminal built around how people actually work with Codex, Claude Code, and other AI CLIs: one window, one project folder, several sessions, and text that stays readable when Arabic and English share the same line.

Most terminals treat mixed-script output as an afterthought. Satr treats it as the point — Unicode bidirectional layout, Arabic shaping, and IME preedit on the terminal surface — while still behaving like a modern emulator for TUI tools (alternate screen, bracketed paste, mouse reporting, scrollback search).

Built by [Mohamad Salah](https://mohamadsala.me/). Current release: **0.3.0**.

## What Satr is for

Satr answers a narrow question: *how do I keep AI CLI work organized per project, without fighting the terminal when the text is not plain ASCII?*

| You want to… | Satr gives you… |
| --- | --- |
| Work in a repo with shell + AI sessions side by side | Project-labelled tabs that restore on reopen |
| Paste prompts, code, and mixed Arabic/English text | A terminal that shapes and lays out mixed script correctly |
| Jump back through long CLI output | Scrollback search (`Ctrl+Shift+F`) and readable transcript export |
| Pick up where Codex or another tool left off | Resume launchers and saved workspace state |
| Stay local | No telemetry, no bundled model, no app-level network client |

Satr is **not** an AI product. It launches tools already on your `PATH` and renders what they print.

## How a session works

1. **Pick a project directory** — tabs are scoped to a folder.
2. **Open a session** — shell, Codex, Claude Code, Agy, Omp, or a resumed CLI.
3. **Type or paste in the terminal** — review output, search scrollback, copy selections.
4. **Close the window** — layout, tabs, font, and geometry are saved; Codex tabs reopen via `codex resume`.

Multi-line paste requires bracketed-paste support from the running CLI. The path bar follows shell OSC 7 when the shell emits it.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+Shift+T | New shell tab |
| Ctrl+Shift+P | Command palette (all session types) |
| Ctrl+Tab / Ctrl+Shift+Tab | Switch tabs |
| Ctrl+Shift+W | Close current tab |
| Ctrl+Shift+C | Copy selection |
| Ctrl+Shift+F | Search scrollback |
| F3 / Shift+F3 | Next / previous match |
| Ctrl+Shift+R | Restart ended session |
| Ctrl+Shift+PageUp / PageDown | Move tab |
| Ctrl+V | Paste into terminal |
| Shift + drag | Select while mouse is captured by a TUI |

## Downloads

| Platform | Package |
| --- | --- |
| Windows x64 | [Release installer and portable ZIP](https://github.com/Mo999salah/Satr/releases) |
| Linux x64 | [Portable `.tar.gz`](https://github.com/Mo999salah/Satr/releases) |
| Source | GitHub source archive |

Packages ship the .NET runtime. Install AI tools separately. The Windows installer is unsigned. No AUR package is published.

## Installation

### Windows

Run the installer, or extract the **entire** portable ZIP and launch `Satr.exe`. Keep native DLLs beside the executable. ConPTY requires Windows 10 build 17763 or newer. Installs per user as **Satr Preview**, separate from the earlier WPF build.

### Linux (x86_64)

X11/XWayland is the default. Native Wayland is an experimental opt-in.

```bash
sudo pacman -S --needed libx11 libice libsm libxrandr libxi libxcursor fontconfig freetype2 icu openssl zlib ttf-dejavu noto-fonts noto-fonts-emoji xorg-xwayland
mkdir satr && tar -xzf Satr-0.3.0-linux-x64.tar.gz -C satr && cd satr
chmod +x Satr && ./Satr
# Optional native Wayland:
SATR_BACKEND=wayland ./Satr
# Optional menu entry + ~/.local/bin/satr:
bash install-linux.sh
```

`install-linux.sh` installs to `$XDG_DATA_HOME/satr` (or `~/.local/share/satr`), adds a desktop entry, and removes legacy `satr-preview` launchers. `bash uninstall-linux.sh` removes the app but keeps workspace data. Do not run either script with `sudo`.

## Capabilities

**Terminal core** — ANSI parser, scrollback with resize reflow, alternate screen, application cursor keys, SGR mouse, focus reporting, OSC 7 working-directory tracking, OSC 8 hyperlinks, OSC 133 prompt marks.

**Mixed script** — Smart RTL spans inside LTR rows, Arabic shaping, emoji-width cells, IME preedit overlay.

**Workspace** — Atomic JSON persistence with backup recovery, custom tab titles, font family/size, scrollback depth, window geometry, clipboard-image file paths.

**Platform** — Avalonia UI on Windows (ConPTY) and Linux (PTY) via Porta.Pty.

Launch profiles exist for Codex, `codex resume`, Claude Code, Agy, Agy `--continue`, Omp, Omp `--continue`, and the system shell. Compatibility with every CLI version is not guaranteed.

## Data and privacy

| Platform | Workspace location |
| --- | --- |
| Windows | `%LocalAppData%\Satr` |
| Linux | `$XDG_STATE_HOME/Satr` or `~/.local/state/Satr` |

`SATR_DATA_DIR` overrides the path. Existing `Satr-Preview` data migrates once to `Satr`. Do not point two running installs at the same directory. Clipboard images land in `clipboard-images/` locally; Satr does not upload them. Uninstalling preserves workspace files.

## Build from source

Requires **.NET SDK 10**. PowerShell 7 for packaging; Inno Setup 6 optional for the Windows installer.

```bash
git clone https://github.com/Mo999salah/Satr.git && cd Satr
dotnet build src/Satr/Satr.csproj -c Release
dotnet run --project src/Satr/Satr.csproj
```

```powershell
./scripts/Build.ps1 -Dotnet dotnet
# Windows installer:
./scripts/Build.ps1 -Dotnet dotnet -Compiler 'C:/path/to/ISCC.exe'
```

Artifacts land in `artifacts/`. CI builds archives on push and pull request but does not publish releases or run runtime checks. Before a release:

```bash
dotnet run --project tests/Satr.PortChecks/Satr.PortChecks.csproj
dotnet run --project tests/Satr.BufferTests/Satr.BufferTests.csproj
```

## Architecture

| File | Role |
| --- | --- |
| `TerminalBuffer.cs` | ANSI parsing, screen state, scrollback |
| `SmartRtl.cs` | Bidirectional span detection |
| `TerminalView.cs` | Layout, shaping, selection, drawing |
| `PtySession.cs` | PTY lifecycle and UTF-8 I/O |
| `MainWindow.cs` | Workspace UI and session control |
| `WorkspaceStore.cs` | Persistence and recovery |

Stack: **Avalonia 12.1.2**, **Porta.Pty 2.2.2**, **Unicode.Bidi 0.3.18**.

## Known limitations

- Pasted image paths depend on what the active CLI accepts.
- Directory tracking needs shell OSC 7; otherwise the launch path is shown.
- Native Wayland is opt-in; X11/XWayland remains the supported default.
- Installer signing, clean-machine validation, and live CLI compatibility testing remain release work.

## Contributing and lineage

Bug reports and focused pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

Satr is MIT-licensed. It is an independently maintained derivative of [RtlTerminal](https://github.com/mirbehnam/RtlTerminal): parser and rendering lineage stay attributed; this project replaces WPF, Registry settings, Windows window helpers, and the original session bridge with a cross-platform Avalonia app. Not affiliated with OpenAI, Anthropic, or the upstream author.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [licenses/](licenses/) for copyright and dependency notices.
