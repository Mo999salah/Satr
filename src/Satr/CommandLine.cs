namespace Satr;

internal readonly record struct StartupOptions(bool RestoreWorkspace, string[]? Command, bool ShowHelp)
{
    public static StartupOptions Parse(string[] args)
    {
        if (args.Length == 0) return new(false, null, false);
        if (args is ["--workspace"]) return new(true, null, false);
        if (args is ["--help"] or ["-h"]) return new(false, null, true);
        if (args[0] is not ("--command" or "-e"))
            throw new ArgumentException("Unsupported option. Use --workspace, --command, or -e.");

        var command = args[1..];
        if (command.Length == 0) throw new ArgumentException("--command requires a program.");
        if (command.Any(string.IsNullOrWhiteSpace) || command.Any(value => value.Any(char.IsControl)))
            throw new ArgumentException("Command arguments cannot be empty or contain control characters.");
        return new(false, command, false);
    }

    public static string HelpText => "Usage: satr [--workspace | --command|-e <program> [arguments...]]";
}
