using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Tm2Ac.Core;

/// <summary>Finds Steam library folders and installed apps (SPEC §2.3).</summary>
public static partial class SteamLibraries
{
    public const int AssettoCorsaAppId = 244210;
    public const int TmNationsForeverAppId = 11020;
    public const int Trackmania2020AppId = 2225070;

    /// <summary>Steam's install directory from the registry, or null when Steam isn't installed.</summary>
    [SupportedOSPlatform("windows")]
    public static string? FindSteamRoot()
    {
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            if (Registry.LocalMachine.OpenSubKey(key)?.GetValue("InstallPath") is string path && Directory.Exists(path))
            {
                return path;
            }
        }

        return Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") is string userPath && Directory.Exists(userPath)
            ? userPath
            : null;
    }

    /// <summary>
    /// Finds the install folder of <paramref name="appId"/>. Prefers the library whose libraryfolders.vdf lists the app,
    /// since stale copies can exist in other libraries.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? FindApp(int appId, string commonFolderName)
    {
        var steamRoot = FindSteamRoot();
        if (steamRoot is null)
        {
            return null;
        }

        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        var libraries = File.Exists(vdfPath) ? ParseLibraryFolders(File.ReadAllText(vdfPath)) : [new SteamLibrary(steamRoot, new HashSet<int>())];

        var candidates = libraries.Where(l => l.AppIds.Contains(appId)).Concat(libraries);
        return candidates
            .Select(l => Path.Combine(l.Path, "steamapps", "common", commonFolderName))
            .FirstOrDefault(Directory.Exists);
    }

    /// <summary>Parses the library entries of a libraryfolders.vdf (path + installed app ids).</summary>
    public static IReadOnlyList<SteamLibrary> ParseLibraryFolders(string vdf)
    {
        ArgumentNullException.ThrowIfNull(vdf);
        var libraries = new List<SteamLibrary>();
        foreach (Match block in LibraryBlockRegex().Matches(vdf))
        {
            var path = block.Groups["path"].Value.Replace(@"\\", @"\", StringComparison.Ordinal);
            var apps = AppIdRegex().Matches(block.Groups["apps"].Value).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            libraries.Add(new SteamLibrary(path, apps));
        }

        return libraries;
    }

    [GeneratedRegex("\"path\"\\s*\"(?<path>[^\"]+)\"(?<rest>(?:(?!\"path\").)*?)\"apps\"\\s*\\{(?<apps>[^}]*)\\}", RegexOptions.Singleline)]
    private static partial Regex LibraryBlockRegex();

    [GeneratedRegex("\"(\\d+)\"\\s*\"\\d+\"")]
    private static partial Regex AppIdRegex();
}

public sealed record SteamLibrary(string Path, IReadOnlySet<int> AppIds);

/// <summary>Assetto Corsa install helpers.</summary>
public static class AssettoCorsa
{
    [SupportedOSPlatform("windows")]
    public static string? FindInstall() => SteamLibraries.FindApp(SteamLibraries.AssettoCorsaAppId, "assettocorsa");

    public static string TracksDirectory(string acRoot) => Path.Combine(acRoot, "content", "tracks");

    /// <summary>CSP version from extension/config/data_manifest.ini, or null when CSP isn't installed.</summary>
    public static string? FindCspVersion(string acRoot)
    {
        var manifest = Path.Combine(acRoot, "extension", "config", "data_manifest.ini");
        if (!File.Exists(manifest))
        {
            return null;
        }

        return File.ReadLines(manifest)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("SHADERS_PATCH=", StringComparison.Ordinal))?["SHADERS_PATCH=".Length..];
    }
}
