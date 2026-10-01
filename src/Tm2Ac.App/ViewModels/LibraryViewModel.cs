using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tm2Ac.AcTrack;
using Tm2Ac.App.Services;
using Tm2Ac.Core;
using Tm2Ac.Pipeline;

namespace Tm2Ac.App.ViewModels;

/// <summary>Tracks this tool installed into AC (found by their conversion-report.json).</summary>
internal sealed partial class LibraryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;

    public LibraryViewModel(AppServices services, IDialogs dialogs)
    {
        _services = services;
        _dialogs = dialogs;
        _services.LibraryChanged += (_, _) => Refresh();
    }

    public ObservableCollection<InstalledTrackViewModel> Tracks { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReconvertCommand))]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    public void Refresh()
    {
        Tracks.Clear();
        if (_services.TracksDirectory is not { } dir)
        {
            Status = "Assetto Corsa wasn't found. Set its folder in Settings.";
            return;
        }

        foreach (var track in InstalledTracks.Scan(dir))
        {
            var item = new InstalledTrackViewModel(track);
            Tracks.Add(item);
            _ = item.LoadPreviewAsync();
        }

        Status = Tracks.Count == 0 ? "No converted tracks yet. Find one under Browse." : $"{Tracks.Count} tracks, {Format.Size(Tracks.Sum(t => t.Track.SizeBytes))}";
    }

    [RelayCommand]
    private void OpenFolder(InstalledTrackViewModel item) => _dialogs.Open(item.Track.Directory);

    [RelayCommand]
    private void OpenReport(InstalledTrackViewModel item) => _dialogs.Open(Path.Combine(item.Track.Directory, AcTrackWriter.ReportFileName));

    private static bool HasTmxPage(InstalledTrackViewModel? item) => item?.TmxUri is not null;

    [RelayCommand(CanExecute = nameof(HasTmxPage))]
    private void OpenOnTmx(InstalledTrackViewModel item)
    {
        if (item.TmxUri is { } uri)
        {
            _dialogs.Open(uri.ToString());
        }
    }

    [RelayCommand]
    private void Uninstall(InstalledTrackViewModel item)
    {
        if (!_dialogs.Confirm("Uninstall track", $"Delete {item.Track.Name} ({item.Track.TrackId}) from Assetto Corsa?"))
        {
            return;
        }

        try
        {
            InstalledTracks.Uninstall(item.Track);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error("Uninstall failed", $"{e.Message}\n\nIs AC or Content Manager using the track?");
        }

        _services.NotifyLibraryChanged();
    }

    private bool CanReconvert(InstalledTrackViewModel? item) => !IsBusy && item?.TmxUri is not null;

    /// <summary>Converts the track again with the default options from Settings (e.g. after an update of this tool).</summary>
    [RelayCommand(CanExecute = nameof(CanReconvert))]
    private async Task ReconvertAsync(InstalledTrackViewModel item)
    {
        IsBusy = true;
        var progress = new Progress<string>(step => Status = $"{item.Track.Name}: {step}…");
        try
        {
            var settings = _services.Settings;
            var options = new ConversionOptions { Scale = settings.DefaultScale, Pitboxes = settings.DefaultPitboxes, DefaultGrass = settings.DefaultGrass };
            var result = await _services.Conversions.ConvertAsync(item.GameValue!.Value, item.Track.TmxId, options, progress, CancellationToken.None).ConfigureAwait(true);
            _services.NotifyLibraryChanged();
            Status = $"Re-converted {result.TrackId}.";
        }
#pragma warning disable CA1031 // shown to the user
        catch (Exception e)
#pragma warning restore CA1031
        {
            Status = $"Re-convert failed: {e.Message}";
            AppLog.Write(e);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

internal sealed partial class InstalledTrackViewModel(InstalledTrack track) : ObservableObject
{
    public InstalledTrack Track { get; } = track;
    public TmGame? GameValue { get; } = TmGames.TryParse(track.Game, out var game) ? game : null;
    public Uri? TmxUri => GameValue is { } game && Track.TmxId > 0 ? new Uri(game.ExchangeBaseUri(), $"trackshow/{Track.TmxId}") : null;
    public string Details => $"{Track.TrackId}  ·  converted {Track.ConvertedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {Format.Size(Track.SizeBytes)}";

    [ObservableProperty]
    public partial BitmapSource? Preview { get; set; }

    public async Task LoadPreviewAsync()
    {
        if (Track.PreviewPath is { } path)
        {
            Preview = await Images.LoadAsync(path, 240).ConfigureAwait(true);
        }
    }
}
