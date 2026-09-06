# Contributing

Focused bug reports, accessibility improvements and small pull requests are welcome.

## Bug reports

Include the OS version, desktop session (X11 or Wayland), Satr version, font, CLI version, reproduction steps, expected behavior and actual behavior. For rendering problems, provide a minimal mixed-script example. Remove credentials, private prompts and project data from screenshots or logs.

## Development

Install .NET SDK 10 and build with `dotnet build src/Satr/Satr.csproj -c Release`.
Keep changes focused. Preserve logical Unicode order in stored or transmitted text; shaping belongs in the renderer. Retain copyright and dependency notices. Do not add telemetry or hard-code credentials or personal paths.

Describe the validation actually performed in your pull request. Distinguish a build from GUI or CLI validation. The optional checks under `tests/` are not run by the packaging workflow. Add a small reproducible check for non-trivial behavior. Do not make paid AI requests merely to build the project.

Contributions are distributed under the MIT license.
