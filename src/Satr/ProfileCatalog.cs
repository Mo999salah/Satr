namespace Satr;

internal enum ProfileKind { Shell, Agent }

internal enum ToolPresence
{
    Installed,
    Available
}

internal readonly record struct ProfileDefinition(
    string Id,
    string DisplayName,
    string MenuLabel,
    string Hint,
    string PaletteLabel,
    string TabLabel,
    string PickerLabel,
    ProfileKind Kind,
    string? Executable,
    string[] Arguments,
    string? ResumeId,
    string? FreshId,
    string SetupHint);

internal readonly record struct LaunchPlan(string App, string[] Arguments);

internal static class ProfileCatalog
{
    public static readonly ProfileDefinition[] All =
    [
        new("Shell", "Shell", "System shell", "Fast shell, no AI", "Session: system shell", "Shell", "Shell terminal",
            ProfileKind.Shell, null, [], null, null, ""),
        new("Codex", "Codex", "Codex — new", "OpenAI agent", "Session: new Codex", "Codex", "Codex",
            ProfileKind.Agent, "codex", [], "CodexResume", null,
            "Install the `codex` CLI and add it to PATH. Satr does not run installers."),
        new("CodexResume", "Codex", "Codex — resume", "Continue latest conversation", "Session: resume Codex", "Codex · resume", "Codex — resume",
            ProfileKind.Agent, "codex", ["resume"], null, "Codex",
            "Install the `codex` CLI and add it to PATH. Satr does not run installers."),
        new("Claude", "Claude Code", "Claude Code", "Anthropic agent", "Session: Claude Code", "Claude", "Claude Code",
            ProfileKind.Agent, "claude", [], null, null,
            "Install the `claude` CLI and add it to PATH. Satr does not run installers."),
        new("Agy", "Agy", "Agy — new", "Multi-model agent", "Session: new Agy", "Agy", "Agy",
            ProfileKind.Agent, "agy", [], "AgyResume", null,
            "Install the `agy` CLI and add it to PATH. Satr does not run installers."),
        new("AgyResume", "Agy", "Agy — resume", "Continue latest", "Session: resume Agy", "Agy · resume", "Agy — resume",
            ProfileKind.Agent, "agy", ["--continue"], null, "Agy",
            "Install the `agy` CLI and add it to PATH. Satr does not run installers."),
        new("Omp", "Omp", "Omp — new", "Pi multi-model agent", "Session: new Omp", "Omp", "Omp",
            ProfileKind.Agent, "omp", [], "OmpResume", null,
            "Install the `omp` CLI and add it to PATH. Satr does not run installers."),
        new("OmpResume", "Omp", "Omp — resume", "Continue previous session", "Session: resume Omp", "Omp · resume", "Omp — resume",
            ProfileKind.Agent, "omp", ["--continue"], null, "Omp",
            "Install the `omp` CLI and add it to PATH. Satr does not run installers."),
    ];

    public static ProfileDefinition? Find(string id)
    {
        foreach (var profile in All)
            if (profile.Id == id) return profile;
        return null;
    }

    public static bool IsKnown(string id) => Find(id) is not null;
    public static bool IsAgent(string id) => Find(id) is { Kind: ProfileKind.Agent };
    public static bool IsFreshAgent(string id) => Find(id) is { Kind: ProfileKind.Agent, FreshId: null };
    public static string ToolName(string id) => Find(id)?.Executable ?? "";
    public static string ResumeOf(string id) => Find(id)?.ResumeId ?? id;
    public static string? FreshOf(string id) => Find(id)?.FreshId;
    public static string TabLabelOf(string id) => Find(id)?.TabLabel ?? id;

    public static ToolPresence Presence(string id, Func<string, string?> find)
    {
        var def = Find(id);
        if (def is not { } profile) return ToolPresence.Available;
        if (profile.Kind == ProfileKind.Shell) return ToolPresence.Installed;
        return find(profile.Executable!) is not null ? ToolPresence.Installed : ToolPresence.Available;
    }

    public static bool IsLaunchable(string id, Func<string, string?> find) =>
        Presence(id, find) == ToolPresence.Installed;

    public static string MenuCaption(ProfileDefinition def, ToolPresence presence) =>
        presence == ToolPresence.Installed
            ? def.MenuLabel + " — " + def.Hint
            : def.MenuLabel + " — Available (setup)";

    public static string PaletteCaption(ProfileDefinition def, ToolPresence presence) =>
        presence == ToolPresence.Installed
            ? def.PaletteLabel
            : def.PaletteLabel + " — Available (setup)";

    public static LaunchPlan ResolveLaunch(string profile, Func<string, string?> find)
    {
        if (!IsKnown(profile)) throw new NotSupportedException($"Unsupported session profile: {profile}. Its saved metadata is retained.");
        if (Find(profile) is { Kind: ProfileKind.Agent, Executable: { } tool } def)
        {
            var app = find(tool) ?? throw new FileNotFoundException($"{tool} not found in PATH. Install it first, then reopen Satr.");
            var extra = def.Arguments;
            if (OperatingSystem.IsWindows() && (Path.GetExtension(app).Equals(".cmd", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(app).Equals(".bat", StringComparison.OrdinalIgnoreCase)))
            {
                if (app.Contains('%') || app.Contains('"') || app.Any(char.IsControl))
                    throw new NotSupportedException("Move the CLI wrapper to a path without percent signs, quotes or control characters.");
                var cmdLine = "\"\"" + app + "\"" + (extra.Length == 0 ? "" : " " + string.Join(" ", extra)) + "\"";
                return new LaunchPlan(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/D", "/V:OFF", "/S", "/C", cmdLine]);
            }
            return new LaunchPlan(app, extra);
        }

        if (OperatingSystem.IsWindows())
        {
            var app = find("pwsh") ?? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return new LaunchPlan(app, ["-NoLogo"]);
        }

        var shell = Environment.GetEnvironmentVariable("SHELL");
        var unix = shell is not null && Path.IsPathFullyQualified(shell) && File.Exists(shell) ? shell : "/bin/bash";
        return new LaunchPlan(unix, ["-i"]);
    }
}
