using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tm2Ac.App.Services;
using Tm2Ac.Core;

namespace Tm2Ac.App.ViewModels;

/// <summary>Paths (auto-detected unless overridden), conversion defaults and the cache.</summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;

    public SettingsViewModel(AppServices services, IDialogs dialogs)
    {
        _services = services;
        _dialogs = dialogs;
        var s = services.Settings;
        AcPath = s.AcPath ?? "";
        TmnfPath = s.TmnfPath ?? "";
        DefaultScale = s.DefaultScale.ToString("0.##", CultureInfo.InvariantCulture);
        DefaultPitboxes = s.DefaultPitboxes;
        DefaultGrass = s.DefaultGrass;
    }

    /// <summary>Override for the AC folder; empty = auto-detect.</summary>
    [ObservableProperty]
    public partial string AcPath { get; set; }

    /// <summary>Override for the TMNF folder; empty = auto-detect.</summary>
    [ObservableProperty]
    public partial string TmnfPath { get; set; }

    [ObservableProperty]
    public partial string DefaultScale { get; set; }

    [ObservableProperty]
    public partial int DefaultPitboxes { get; set; }

    [ObservableProperty]
    public partial bool DefaultGrass { get; set; }

    [ObservableProperty]
    public partial string AcStatus { get; set; } = "";

    [ObservableProperty]
    public partial string CspStatus { get; set; } = "";

    [ObservableProperty]
    public partial string TmnfStatus { get; set; } = "";

    [ObservableProperty]
    public partial string CacheStatus { get; set; } = "";

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    /// <summary>AC and TMNF were both found (CSP is only warned about).</summary>
    [ObservableProperty]
    public partial bool IsReady { get; set; }

    public static string LegalNotice =>
        "Tm2Ac reads block meshes and textures from your own Trackmania install. Converted tracks contain Nadeo/Ubisoft " +
        "assets: they are for your personal use only and must not be redistributed. Track designs belong to their authors on " +
        "Trackmania Exchange.";

#pragma warning disable CA1822 // instance member so XAML can bind to it
    public string Legal => LegalNotice;
#pragma warning restore CA1822

    /// <summary>Re-checks what was detected with the current settings.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var s = _services.Settings;
        var ac = s.ResolveAcPath();
        var csp = ac is null ? null : AssettoCorsa.FindCspVersion(ac);
        var tmnf = s.ResolveTmnfPath();
        AcStatus = ac is null ? "✗ Not found. Set the folder that contains acs.exe." : $"✓ {ac}";
        CspStatus = ac is null ? "" : csp is null ? "✗ Custom Shaders Patch not found. Converted tracks need CSP." : $"✓ Custom Shaders Patch {csp}";
        TmnfStatus = tmnf is null ? "✗ Not found. Install TrackMania Nations Forever (free on Steam) or set its folder." : $"✓ {tmnf}";
        IsReady = ac is not null && tmnf is not null;
        CacheStatus = $"{Tm2AcPaths.CacheDirectory}  ({Format.Size(DirectorySize(Tm2AcPaths.CacheDirectory))})";
    }

    [RelayCommand]
    private void BrowseAc()
    {
        if (_dialogs.PickFolder("Assetto Corsa folder", AcPath) is { } folder)
        {
            AcPath = folder;
        }
    }

    [RelayCommand]
    private void BrowseTmnf()
    {
        if (_dialogs.PickFolder("TrackMania Nations Forever folder", TmnfPath) is { } folder)
        {
            TmnfPath = folder;
        }
    }

    [RelayCommand]
    public void Save()
    {
        var s = _services.Settings;
        s.AcPath = string.IsNullOrWhiteSpace(AcPath) ? null : AcPath.Trim();
        s.TmnfPath = string.IsNullOrWhiteSpace(TmnfPath) ? null : TmnfPath.Trim();
        if (float.TryParse(DefaultScale.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale is >= 0.25f and <= 4f)
        {
            s.DefaultScale = scale;
        }

        DefaultScale = s.DefaultScale.ToString("0.##", CultureInfo.InvariantCulture);
        s.DefaultPitboxes = Math.Clamp(DefaultPitboxes, 1, 64);
        DefaultPitboxes = s.DefaultPitboxes;
        s.DefaultGrass = DefaultGrass;
        try
        {
            s.Save();
            Message = "Saved.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = $"Couldn't save settings: {e.Message}";
        }

        Refresh();
        _services.NotifyLibraryChanged();
    }

    [RelayCommand]
    private void ClearCache()
    {
        if (!_dialogs.Confirm("Clear cache", "Delete downloaded maps, replays and images? They are downloaded again when needed."))
        {
            return;
        }

        try
        {
            if (Directory.Exists(Tm2AcPaths.CacheDirectory))
            {
                Directory.Delete(Tm2AcPaths.CacheDirectory, recursive: true);
            }

            Message = "Cache cleared.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = $"Some cache files are in use: {e.Message}";
        }

        Refresh();
    }

    [RelayCommand]
    private void OpenLogs() => _dialogs.Open(Directory.CreateDirectory(Tm2AcPaths.LogsDirectory).FullName);

    private static long DirectorySize(string path) =>
        Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
}
