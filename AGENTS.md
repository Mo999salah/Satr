# Satr Agent Rules

These rules apply to Satr. Inherited agent rules still apply unless this file is more specific.

## Scope and ownership

- Treat `src/Satr/` as source of truth. Treat `artifacts/` as generated output; never hand-edit it.
- Keep a change at its owning boundary: `TerminalBuffer` owns terminal protocol/state, `PtySession` owns PTY lifetime and I/O, `TerminalView` owns layout/shaping/drawing, `WorkspaceStore` owns persistence/recovery, and `MainWindow*` owns UI orchestration.
- Reuse existing patterns and dependencies. Add a dependency or abstraction only when existing .NET, Avalonia, or project code cannot meet a confirmed requirement.
- Never change unrelated dirty files. Do not change licensing, attribution, or third-party notices unless the task requires it.
- Prefer minimal, focused diffs. Do not refactor unrelated code or introduce abstractions without a concrete need.

## Development vs release

Treat these as separate states:

- `main` is the current development branch.
- Local development builds represent `main`, not the latest GitHub Release.
- `satr-dev` on Linux and `Satr Dev` on Windows are development builds.
- A version tag such as `v0.5.1` is an immutable release snapshot.
- GitHub Release artifacts must be built from the exact tagged commit.
- Pushing changes to `main` does not update an existing GitHub Release.

Never assume that the latest source code on `main` is present in the latest published Release.

When investigating a behavior difference, first identify whether each tested copy comes from `main`, a development build, a version tag, or a published Release artifact. Do not treat those as equivalent.

## Cross-platform behavior and parity

- Treat every `src/Satr` change as Linux and Windows work. Prefer portable .NET and Avalonia APIs.
- Put required OS-specific code behind the narrowest named boundary. Preserve equivalent user-visible behavior on Linux and Windows, or document the unsupported platform explicitly.
- PTY backends may differ, but session creation, input, resize, cancellation, exit status, and workspace recovery require equivalent contracts.
- Use `Dispatcher.UIThread` for every Avalonia control access from background work. Keep long-running I/O off the UI thread.
- Unless a feature is explicitly platform-specific, verify equivalent user-visible behavior on Windows and Linux.
- For shared UI changes, consider keyboard shortcuts, context menus, sidebar/workspace UI, terminal interaction, settings, persistence, and platform-specific input behavior.
- A successful build or manual test on one operating system is not sufficient evidence that a shared feature works on the other.

Do not intentionally ship different feature sets between Windows and Linux unless the difference is required by the platform and documented.

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
- For shared UI behavior, the relevant checks must pass on both Windows and Linux through local validation or CI before declaring cross-platform parity.
- A release requires the CI matrix to pass on Windows and Linux, including desktop smoke checks.
- Do not claim native IME, Wayland, X11, Windows console, or live CLI compatibility without evidence from that target.
- Do not treat a test that could not run as a passing test.
- Do not start Satr, a shell, or an AI CLI locally without explicit user permission. Never make paid AI requests for validation.
- Before considering a change complete, inspect the actual diff and report the exact validation performed.

## Development installs

- Keep the released install stable while testing source work through isolated development installs.
- On Linux, stable is `satr` and development is `satr-dev`; use `scripts/dev-linux.sh` for development testing.
- On Windows, stable is the normal Satr installation and development is `Satr Dev`; use `scripts/Dev-Windows.ps1` for development testing.
- Development installs require their own `SATR_DATA_DIR`; never share workspace state or a session lock with the released install.
- Do not compare a development build from one platform with an older stable Release from another platform when evaluating parity.

## Release provenance

Every artifact belonging to one Release must originate from the same tagged source commit.

For `vX.Y.Z`, all supported release outputs must correspond to that exact tag, including:

- Windows portable ZIP
- Windows installer
- Linux tarball
- Debian package
- Arch/CachyOS package
- Pacman repository metadata generated from those packages
- checksum manifest

Do not replace source files or build scripts with newer versions from `main` while building an existing tag.

If release tooling is broken in a tag, fix the tooling on `main` and create a new fixed release/tag as appropriate. Never silently mix source revisions to rescue an old tag.

## Release policy

- Normal pushes to `main` are development updates and validation only; they must not publish or mutate a GitHub Release.
- Create a Release only when the user explicitly asks to release/version/publish the project.
- Before a release, assign the intended semantic version in `src/Satr/Satr.csproj` and ensure the tag `vX.Y.Z` exactly matches project version `X.Y.Z`.
- Release only a committed `main` revision tagged `vX.Y.Z`; never reuse or move an existing release tag.
- Build all release artifacts from that exact tag commit.
- GitHub Actions must run required tests before release jobs, build Linux and Windows deliverables from the same tag, verify checksums/digests, and publish only after all required artifacts pass verification.
- If a required artifact, checksum, test, upload, tag, or release step fails, stop: no partial release and no completion claim.
- Installing or upgrading the local machine is separate from publishing a GitHub Release and requires explicit user authorization.

## Immutable releases

After `vX.Y.Z` is published:

- Do not rebuild it from a newer commit.
- Do not silently replace its binaries with builds containing later code.
- Do not move or reuse the tag.
- A failed in-flight draft may be resumed only for the same tagged commit.

If a published release contains a bug, fix it on `main` and publish a new patch version instead of mutating the old release.

## Build identity

Development and release builds should expose enough identity to determine what is actually running:

- application version
- Git commit SHA
- build channel (`Development` or `Release`)
- runtime/platform

Use this information when investigating reports where Windows and Linux appear to behave differently.

## Git boundaries

- Do not commit or push unless the user explicitly asks for it.
- Do not create, move, or publish tags/releases unless the user explicitly asks for it.
- Never rewrite history, force-push, delete a release, or delete a tag unless explicitly requested and the consequences are clear.
- Do not stage unrelated untracked or dirty files.
- A normal development commit/push must not be treated as authorization to create a release.

## Agent workflow

When asked to fix or change Satr:

1. Inspect the relevant current source first.
2. Determine whether the observed behavior comes from `main`, a development build, a version tag, or a published Release artifact.
3. Identify the root cause before proposing or applying changes.
4. For platform-specific reports, inspect both shared code and platform-specific paths.
5. Keep changes minimal and scoped.
6. Run only the relevant checks for the affected area unless the change has broad release risk.
7. For shared UI changes, verify both Linux and Windows parity through the existing CI/test structure.
8. Before changing release or packaging behavior, inspect the complete current release workflow and identify the commit/tag/artifact relationship.
9. Report validation gaps honestly; do not substitute static reasoning for tests that are required but could not run.
10. Do not create a release merely to test whether ordinary development changes work.
