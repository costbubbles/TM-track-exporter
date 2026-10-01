using System.Runtime.Versioning;
using System.Text.Json;

namespace Tm2Ac.Core;

/// <summary>User settings, stored in %APPDATA%\Tm2Ac\settings.json (SPEC §2.3). Missing paths are auto-detected.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(Tm2AcPaths.SettingsDirectory, "settings.json");

    /// <summary>Assetto Corsa folder; null = auto-detect from Steam.</summary>
    public string? AcPath { get; set; }

    /// <summary>TrackMania Nations Forever folder; null = auto-detect.</summary>
    public string? TmnfPath { get; set; }

    public float DefaultScale { get; set; } = 1f;
    public int DefaultPitboxes { get; set; } = 10;

    /// <summary>The user has acknowledged that converted tracks contain Nadeo assets and are for personal use only.</summary>
    public bool LegalNoticeAccepted { get; set; }

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings() : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings(); // a corrupt settings file shouldn't stop the app
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    [SupportedOSPlatform("windows")]
    public string? ResolveAcPath() => !string.IsNullOrWhiteSpace(AcPath) && Directory.Exists(AcPath) ? AcPath : AssettoCorsa.FindInstall();

    [SupportedOSPlatform("windows")]
    public string? ResolveTmnfPath() => !string.IsNullOrWhiteSpace(TmnfPath) && Directory.Exists(TmnfPath) ? TmnfPath : TrackmaniaInstalls.FindTmnf();
}
