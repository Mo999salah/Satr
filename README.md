# Satr

**An Arabic-first terminal workspace for Windows and Linux.**

[Downloads](https://github.com/Mo999salah/Satr/releases/tag/v0.2.0-preview.1) · [Issues](https://github.com/Mo999salah/Satr/issues) · [Contributing](CONTRIBUTING.md) · [MIT license](LICENSE)

Satr combines a terminal with an Arabic prompt editor. Write mixed Arabic and English text in logical Unicode order, transfer it to a CLI, and keep a saved draft for each project session. The application interface currently uses Arabic; this README is in English.

Built by [Mohamad Salah](https://mohamadsala.me/).

> **Early preview:** 0.2.0-preview.1 has been compiled and packaged. Graphical behavior, Linux runtime compatibility, installer execution and AI CLI workflows have not been validated. These downloads are experimental.

## Downloads

| Platform | Package |
| --- | --- |
| Windows x64 | [Installer (.exe)](https://github.com/Mo999salah/Satr/releases/download/v0.2.0-preview.1/Satr-Setup-0.2.0-preview.1-win-x64.exe) |
| Windows x64 | [Portable (.zip)](https://github.com/Mo999salah/Satr/releases/download/v0.2.0-preview.1/Satr-0.2.0-preview.1-win-x64.zip) |
| Linux x64 | [Portable (.tar.gz)](https://github.com/Mo999salah/Satr/releases/download/v0.2.0-preview.1/Satr-0.2.0-preview.1-linux-x64.tar.gz) |
| Source | [Source archive (.zip)](https://github.com/Mo999salah/Satr/releases/download/v0.2.0-preview.1/Satr-0.2.0-preview.1-source.zip) |
| Integrity | [SHA-256 checksums](https://github.com/Mo999salah/Satr/releases/download/v0.2.0-preview.1/SHA256SUMS.txt) |

Packages include the .NET runtime. Install AI tools separately and make them available on `PATH`. The Windows installer is unsigned. No AUR package is published.

## Features implemented

- Arabic prompt editor with RTL/LTR switching, text import/export and retained drafts.
- ANSI rendering with Unicode bidirectional layout and Arabic shaping.
- Project directories, multiple sessions and lazy restoration of saved tabs.
- Launch actions for Codex, `codex resume`, Claude Code and the system shell.
- Bracketed paste, application cursor keys, alternate screen, SGR mouse and focus reporting.
- Selection, clipboard text, scrollback, font-size adjustment and a readable text transcript.
- Atomic workspace saves, backup recovery and draft archiving when closing tabs.
- Shared Avalonia UI with Windows ConPTY and Linux PTY connections through Porta.Pty.

Implemented launch actions do not mean verified compatibility with every CLI version. Satr does not bundle an AI model or subscription.

## Installation

### Windows

Run the installer, or extract the **entire** portable ZIP and open `Satr.exe`. Keep native DLLs and ConPTY host directories beside the executable. The preview installs per user as **Satr Preview**, separately from the earlier WPF version. ConPTY requires Windows 10 build 17763 or newer.

### Arch Linux

The intended setup below still requires validation on a real x86_64 desktop. Use X11 or XWayland; native Wayland is not configured in this preview.

```bash
sudo pacman -S --needed libx11 libice libsm libxrandr libxi libxcursor fontconfig freetype2 icu openssl zlib ttf-dejavu noto-fonts noto-fonts-emoji xorg-xwayland
mkdir satr-preview
tar -xzf Satr-0.2.0-preview.1-linux-x64.tar.gz -C satr-preview
cd satr-preview
chmod +x Satr
./Satr
# Optional per-user installation and application-menu entry:
bash install-linux.sh
```

The script installs into `$XDG_DATA_HOME/satr-preview`, falling back to `~/.local/share/satr-preview`, and creates `~/.local/bin/satr-preview`. Do not run it with `sudo`. Close an existing installation before replacing files.

## Workflow

1. Choose a project directory and open a shell or Codex session.
2. Write a prompt in the Arabic editor.
3. Transfer it with **Ctrl+Enter**. The editor keeps its copy.
4. Review the text in the terminal and press **Enter** there to submit it.

Multi-line transfers are rejected if the session has not enabled bracketed paste. The displayed directory is the starting directory; it does not track `cd`. Restoration launches only the selected tab. Saved Codex tabs reopen through `codex resume`. Closing a tab archives its draft; closing the window saves the workspace and terminates sessions.

| Shortcut | Action |
| --- | --- |
| Ctrl+Shift+E | Focus the prompt editor |
| Ctrl+Enter in the editor | Transfer without submitting |
| Ctrl+Shift+T | Open a shell tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Switch tabs |
| Ctrl+Shift+W | Close the current tab |
| Ctrl+Shift+C | Copy selected terminal text |
| Ctrl+V in the terminal | Paste clipboard text |
| Shift + mouse selection | Select while a CLI captures mouse input |

## Data and privacy

Satr adds no telemetry, updater, API-key store or application network client. Launched CLIs manage their own authentication and network activity. Drafts are plain text; avoid retaining secrets in them.

| Platform | Workspace location |
| --- | --- |
| Windows | `%LocalAppData%\Satr-Preview` |
| Linux | `$XDG_STATE_HOME/Satr-Preview` or `~/.local/state/Satr-Preview` |

`SATR_DATA_DIR` overrides the location. The old WPF workspace is not automatically imported or modified. Do not share a data directory between running versions. Uninstalling on Windows preserves personal drafts.

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

Optional checks are available but were **not executed for this preview**:

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

- Arabic shaping, mixed-script selection, input methods, fonts and DPI need Linux runtime validation.
- Codex resizing, paste, mouse reporting, interrupts and process cleanup need runtime validation.
- Direct terminal IME and custom-surface accessibility need review; the separate editor and text transcript offer alternative input and reading paths.
- Clipboard images, the earlier font-family dialog and detailed window-placement settings are not ported. Font size and RTL preference are persisted.
- Clean-machine installer validation, binary signing and native Wayland integration remain future work.

## Contributing and license

Focused bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

Satr is open source under the [MIT license](LICENSE). It is an independently maintained derivative of [RtlTerminal](https://github.com/mirbehnam/RtlTerminal), not an engine written from scratch. Parser and rendering lineage remain attributed; the cross-platform project replaces WPF, Registry settings, Windows window helpers and the original session bridge.

Original copyright and dependency notices are preserved in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [licenses/](licenses/). Satr is not affiliated with OpenAI, Anthropic or the upstream project.
