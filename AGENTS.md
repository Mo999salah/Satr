# Satr Agent Rules

These rules apply to Satr. Inherited agent rules still apply unless this file is more specific.

## Scope and ownership

- Treat `src/Satr/` as source of truth. Treat `artifacts/` as generated output; never hand-edit it.
- Keep a change at its owning boundary: `TerminalBuffer` owns terminal protocol/state, `PtySession` owns PTY lifetime and I/O, `TerminalView` owns layout/shaping/drawing, `WorkspaceStore` owns persistence/recovery, and `MainWindow*` owns UI orchestration.
- Reuse existing patterns and dependencies. Add a dependency or abstraction only when existing .NET, Avalonia, or project code cannot meet a confirmed requirement.
- Never change unrelated dirty files. Do not change licensing, attribution, or third-party notices unless the task requires it.

## Cross-platform behavior

- Treat every `src/Satr` change as Linux and Windows work. Prefer portable .NET and Avalonia APIs.
- Put required OS-specific code behind the narrowest named boundary. Preserve equivalent user-visible behavior on Linux and Windows, or document the unsupported platform explicitly.
- PTY backends may differ, but session creation, input, resize, cancellation, exit status, and workspace recovery require equivalent contracts.
- Use `Dispatcher.UIThread` for every Avalonia control access from background work. Keep long-running I/O off the UI thread.

## Terminal correctness and safety

- Preserve logical Unicode order in stored and transmitted text; apply bidi shaping only while rendering.
- Treat PTY output, terminal escape sequences, pasted paths, and links as untrusted input. Never execute a command, open a link, or change a workspace solely because terminal output requests it.
- For ANSI/CSI/OSC, UTF-8, grapheme width, bidi, Arabic shaping, cursor, scrollback, or selection changes: add a focused regression case to `tests/Satr.BufferTests` before declaring the change complete.
- Do not present Satr as a system default terminal until it supports and tests an external-command contract such as `--command` and `-e` on both platforms.

## Async, errors, and persistent state

- Pass `CancellationToken` through owned asynchronous I/O and process work. Dispose PTY sessions, streams, timers, and cancellation sources deterministically.
- Use `async void` only for UI event handlers. Observe failures from every background task.
- Catch only errors that have a defined recovery path; surface actionable errors without exposing credentials or private terminal content.
- Preserve `workspace.json`, backups, drafts, sessions, and `SATR_DATA_DIR` behavior. A state-shape change requires a forward migration, backward-compatible loading where possible, and recovery coverage in `Satr.PortChecks`.
- Never delete user state, session data, installed copies, or backups as part of build, test, update, or uninstall work unless the user explicitly asks.

## Validation

- A successful build is not feature validation. Run the smallest relevant check first:
  - terminal engine or rendering-data change: `Satr.BufferTests`;
  - PTY, workspace, migration, or filesystem change: `Satr.PortChecks`;
  - window, shortcut, or interaction change: `Satr.UiChecks`.
- A release requires the CI matrix to pass on Windows and Linux, including desktop smoke checks. Do not claim native IME, Wayland, X11, Windows console, or live CLI compatibility without evidence from that target.
- Do not start Satr, a shell, or an AI CLI locally without explicit user permission. Never make paid AI requests for validation.

## Development installs

- Keep the released install stable while testing source work through the isolated `satr-dev` development install. Use `scripts/dev-linux.sh` on Linux and `scripts/Dev-Windows.ps1` on Windows.
- Development installs require their own `SATR_DATA_DIR`; never share workspace state or a session lock with the released install.

## Release policy

- Every completed update is a GitHub release unless the user explicitly scopes it as local-only, draft-only, or CI-only.
- Before release, assign the next semantic version in `src/Satr/Satr.csproj`: patch for compatible fixes, minor for compatible features, major for breaking workspace or command-line contracts. Update every versioned download/reference file in the same change.
- Release only a committed `main` revision tagged `vX.Y.Z`; never reuse or move an existing release tag.
- Build and publish all supported deliverables: Linux tarball, Pacman package/database, DEB package, Windows ZIP, Windows installer when Inno Setup is available, and `SHA256SUMS.txt` covering every uploaded binary.
- GitHub Actions must build after tests and create the GitHub Release from the tag. Grant `contents: write` only to the release job. If a required artifact, checksum, test, upload, tag, or release step fails, stop: no partial release and no completion claim.
- Installing or upgrading the local machine is separate from publishing a GitHub release and requires explicit user authorization.

## Git boundaries

- A completed release authorizes the required version commit, tag, push, and GitHub Release only. Never rewrite history, force-push, delete a release, or delete a tag.
- Report exact validation performed and every release asset URL or failed gate.
