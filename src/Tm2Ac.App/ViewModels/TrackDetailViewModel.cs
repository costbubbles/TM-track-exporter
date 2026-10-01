using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tm2Ac.App.Services;
using Tm2Ac.Core;
using Tm2Ac.Pipeline;
using Tm2Ac.Tmx;

namespace Tm2Ac.App.ViewModels;

/// <summary>Scale presets from SPEC §6.</summary>
internal sealed record ScalePreset(string Label, float Scale)
{
    public static IReadOnlyList<ScalePreset> All { get; } =
    [
        new("Original", 1f),
        new("Road car", 1.5f),
        new("GT", 2f),
        new("Formula", 2.5f),
    ];
}

/// <summary>The selected track: details, conversion options and Convert & Install.</summary>
internal sealed partial class TrackDetailViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;

    public TrackDetailViewModel(TrackItemViewModel item, AppServices services, IDialogs dialogs)
    {
        Item = item;
        _services = services;
        _dialogs = dialogs;
        Scale = services.Settings.DefaultScale;
        Pitboxes = services.Settings.DefaultPitboxes;
        DefaultGrass = services.Settings.DefaultGrass;
        _ = LoadAsync();
    }

    public TrackItemViewModel Item { get; }
    public TmxTrack Track => Item.Track;
    public string Description { get; private set; } = "";
    public string Details => string.Join("  ·  ", new[]
    {
        Item.Type,
        TmxEnums.Mood(Track.Mood),
        $"Author time {Item.AuthorTime}",
        $"{Track.Awards} awards",
        $"Uploaded {Track.UploadedAt:yyyy-MM-dd}",
    });

#pragma warning disable CA1822 // instance members so XAML can bind to them
    public IReadOnlyList<ScalePreset> ScalePresets => ScalePreset.All;
#pragma warning restore CA1822
    public ObservableCollection<string> Log { get; } = [];
    public ObservableCollection<ConversionIssue> Issues { get; } = [];

    [ObservableProperty]
    public partial BitmapSource? Screenshot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScaleText))]
    public partial float Scale { get; set; }

    /// <summary>Editable scale; invalid text keeps the previous value.</summary>
    public string ScaleText
    {
        get => Scale.ToString("0.##", CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale is >= 0.25f and <= 4f)
            {
                Scale = scale;
            }

            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial int Pitboxes { get; set; }

    [ObservableProperty]
    public partial bool DefaultGrass { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial bool IsConverting { get; set; }

    [ObservableProperty]
    public partial string? InstalledPath { get; set; }

    [ObservableProperty]
    public partial string Result { get; set; } = "";

    [RelayCommand]
    private void SetScale(ScalePreset preset) => Scale = preset.Scale;

    [RelayCommand]
    private void OpenOnTmx() => _dialogs.Open(Track.PageUri.ToString());

    [RelayCommand]
    private void OpenFolder()
    {
        if (InstalledPath is { } path)
        {
            _dialogs.Open(path);
        }
    }

    private bool CanConvert() => !IsConverting && Item.IsConvertible;

    [RelayCommand(CanExecute = nameof(CanConvert), IncludeCancelCommand = true)]
    private async Task ConvertAsync(CancellationToken cancellationToken)
    {
        IsConverting = true;
        Log.Clear();
        Issues.Clear();
        Result = "";
        InstalledPath = null;
        var progress = new Progress<string>(step => Log.Add(step));
        try
        {
            var options = new ConversionOptions { Scale = Scale, Pitboxes = Math.Clamp(Pitboxes, 1, 64), DefaultGrass = DefaultGrass };
            var result = await _services.Conversions.ConvertAsync(Track.Game, Track.Id, options, progress, cancellationToken).ConfigureAwait(true);
            foreach (var issue in result.Issues)
            {
                Issues.Add(issue);
            }

            InstalledPath = result.Directory;
            Result = $"Installed as {result.TrackId} ({result.Blocks} blocks, {result.Elapsed.TotalSeconds:0.0} s). It's in Content Manager now.";
            _services.NotifyLibraryChanged();
        }
        catch (OperationCanceledException)
        {
            Result = "Cancelled. Nothing was installed.";
        }
#pragma warning disable CA1031 // any failure is shown to the user instead of crashing the app
        catch (Exception e)
#pragma warning restore CA1031
        {
            Result = $"Conversion failed: {e.Message}";
            AppLog.Write(e);
        }
        finally
        {
            IsConverting = false;
        }
    }

    /// <summary>TMX descriptions use BBCode ([b], [url=...], [img]...); the text is kept, the tags dropped.</summary>
    public static string StripBbCode(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[a-zA-Z*]+(=[^\]]*)?\]", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    private async Task LoadAsync()
    {
        var screenshots = Track.ScreenshotCount;
        try
        {
            // Search results don't carry the description or screenshots; the full track record (cached) does.
            var full = await _services.Tmx.GetTrackAsync(Track.Game, Track.Id).ConfigureAwait(true);
            Description = StripBbCode(TmText.StripFormatting(full.Description)).Trim();
            OnPropertyChanged(nameof(Description));
            screenshots = full.ScreenshotCount;
        }
        catch (Exception e) when (e is HttpRequestException or TmxNotFoundException or System.Text.Json.JsonException)
        {
            // The description is optional.
        }

        Screenshot = await Images.LoadTmxAsync(_services.Tmx, Track.Game, Track.Id, screenshots > 0 ? 1 : 0, 900).ConfigureAwait(true) ?? Item.Thumbnail;
    }
}
