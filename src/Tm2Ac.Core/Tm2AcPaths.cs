namespace Tm2Ac.Core;

/// <summary>Per-user folders (SPEC §2.3).</summary>
public static class Tm2AcPaths
{
    public static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tm2Ac");

    public static string LocalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tm2Ac");

    public static string CacheDirectory => Path.Combine(LocalDirectory, "cache");

    public static string LogsDirectory => Path.Combine(LocalDirectory, "logs");
}
