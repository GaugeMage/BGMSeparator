using System;

namespace BgmSeparator;

internal enum BgmCommand
{
    Settings,
    Toggle,
    Enable,
    Disable,
    DiagnosticsToggle,
    DiagnosticsOn,
    DiagnosticsOff,
    Help,
}

internal static class BgmCommandParser
{
    public static BgmCommand Parse(string command, string args)
    {
        if (command.Equals("/bgmsep", StringComparison.OrdinalIgnoreCase)) return Parse(args);
        if (!string.IsNullOrWhiteSpace(args)) return BgmCommand.Help;
        return command.ToLowerInvariant() switch
        {
            "/bgmseptoggle" => BgmCommand.Toggle,
            "/bgmsepon" => BgmCommand.Enable,
            "/bgmsepoff" => BgmCommand.Disable,
            _ => BgmCommand.Help,
        };
    }

    public static BgmCommand Parse(string args)
    {
        var words = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words).ToLowerInvariant() switch
        {
            "" or "config" => BgmCommand.Settings,
            "toggle" => BgmCommand.Toggle,
            "on" or "enable" => BgmCommand.Enable,
            "off" or "disable" => BgmCommand.Disable,
            "diag" => BgmCommand.DiagnosticsToggle,
            "diag on" => BgmCommand.DiagnosticsOn,
            "diag off" => BgmCommand.DiagnosticsOff,
            _ => BgmCommand.Help,
        };
    }
}
