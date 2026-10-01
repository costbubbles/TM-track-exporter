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

    /// <summary>TMNF install folder, printing an error when it can't be found.</summary>
    public static string? FindTmnf()
    {
        var path = OperatingSystem.IsWindows() ? TrackmaniaInstalls.FindTmnf() : null;
        if (path is null)
        {
            Console.Error.WriteLine("TrackMania Nations Forever not found. Install it (free on Steam, app 11020) and run tm2ac doctor.");
        }

        return path;
    }

    /// <summary>Where track folders go: --out if given, else AC's content/tracks (from --ac-path or Steam).</summary>
    public static string? ResolveTracksDirectory(DirectoryInfo? outDirectory, DirectoryInfo? acPath)
    {
        if (outDirectory is not null)
        {
            return outDirectory.FullName;
        }

        var acRoot = acPath?.FullName ?? (OperatingSystem.IsWindows() ? AssettoCorsa.FindInstall() : null);
        if (acRoot is null)
        {
            Console.Error.WriteLine("Assetto Corsa not found. Pass --ac-path <AC folder> or --out <folder>.");
            return null;
        }

        if (AssettoCorsa.FindCspVersion(acRoot) is null)
        {
            Console.Error.WriteLine("Warning: Custom Shaders Patch not found; converted tracks need CSP.");
        }

        return AssettoCorsa.TracksDirectory(acRoot);
    }

    public static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
