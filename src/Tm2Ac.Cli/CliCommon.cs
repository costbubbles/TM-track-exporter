using System.CommandLine;
using System.Globalization;
using Tm2Ac.Core;

namespace Tm2Ac.Cli;

internal static class CliCommon
{
    public static Argument<string> GameArgument() => new("game")
    {
        Description = "Source game: tmnf or tmuf (tm2020 comes later).",
    };

    /// <summary>Parses the game argument, printing an error when it's unknown.</summary>
    public static bool TryGetGame(string value, out TmGame game)
    {
        if (TmGames.TryParse(value, out game))
        {
            return true;
        }

        Console.Error.WriteLine($"Unknown game '{value}'. Use tmnf, tmuf or tm2020.");
        return false;
    }

    /// <summary>Race time as m:ss.fff (or h:mm:ss.fff).</summary>
    public static string FormatTime(int? milliseconds)
    {
        if (milliseconds is not { } ms || ms < 0)
        {
            return "-";
        }

        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : t.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
    }

    public static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
