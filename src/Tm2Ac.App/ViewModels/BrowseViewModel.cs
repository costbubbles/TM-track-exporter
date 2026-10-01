using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tm2Ac.App.Services;
using Tm2Ac.Core;
using Tm2Ac.Pipeline;
using Tm2Ac.Tmx;

namespace Tm2Ac.App.ViewModels;

internal sealed partial class BrowseViewModel : ObservableObject
{
    private const int PageSize = 25;
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private IReadOnlyDictionary<int, string> _tags = new Dictionary<int, string>();
    private TmGame _tagsGame = (TmGame)(-1);
    private long? _after;

    public BrowseViewModel(AppServices services, IDialogs dialogs)
    {
        _services = services;
        _dialogs = dialogs;
        _services.LibraryChanged += (_, _) => MarkInstalled();
    }

    public ObservableCollection<TrackItemViewModel> Results { get; } = [];

    [ObservableProperty]
    public partial TmGame Game { get; set; } = TmGame.Tmnf;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Author { get; set; } = "";

    [ObservableProperty]
    public partial TmxTrackOrder Order { get; set; } = TmxTrackOrder.MostAwards;

    /// <summary>null = any, otherwise a TMX primary type (Race / Laps).</summary>
    [ObservableProperty]
    public partial int? PrimaryType { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial TrackItemViewModel? SelectedTrack { get; set; }

    [ObservableProperty]
    public partial TrackDetailViewModel? Detail { get; set; }

    partial void OnSelectedTrackChanged(TrackItemViewModel? value)
    {
        if (Detail?.IsConverting == true && Detail.Item == value)
        {
            return;
        }

        Detail = value is null ? Detail?.IsConverting == true ? Detail : null : new TrackDetailViewModel(value, _services, _dialogs);
    }

    // Changing a filter chip searches right away; text fields search on Enter.
    partial void OnGameChanged(TmGame value) => SearchCommand.Execute(null);
    partial void OnOrderChanged(TmxTrackOrder value) => SearchCommand.Execute(null);
    partial void OnPrimaryTypeChanged(int? value) => SearchCommand.Execute(null);

    [RelayCommand]
    private Task SearchAsync(CancellationToken cancellationToken)
    {
        _after = null;
        Results.Clear();
        return LoadPageAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasMore))]
    private Task LoadMoreAsync(CancellationToken cancellationToken) => LoadPageAsync(cancellationToken);

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        Status = "Searching TMX…";
        try
        {
            var game = Game;
            if (_tagsGame != game)
            {
                _tags = await _services.Tmx.GetTagsAsync(game, cancellationToken).ConfigureAwait(true);
                _tagsGame = game;
            }

            var query = new TmxSearchQuery
            {
                Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
                Author = string.IsNullOrWhiteSpace(Author) ? null : Author.Trim(),
                Order = Order,
                PrimaryType = PrimaryType,
                Count = PageSize,
                After = _after,
            };
            var page = await _services.Tmx.SearchTracksAsync(game, query, cancellationToken).ConfigureAwait(true);
            var installed = InstalledIds();
            var items = page.Results.Select(t => new TrackItemViewModel(t, _tags) { IsInstalled = installed.Contains((t.Game.Id(), t.Id)) }).ToList();
            foreach (var item in items)
            {
                Results.Add(item);
            }

            _after = page.Results.Count > 0 ? page.Results[^1].Id : _after;
            HasMore = page.More;
            Status = Results.Count == 0 ? "No tracks found." : $"{Results.Count} tracks";
            await Task.WhenAll(items.Select(i => i.LoadThumbnailAsync(_services.Tmx, cancellationToken))).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "";
        }
        catch (Exception e) when (e is HttpRequestException or TmxNotFoundException or System.Text.Json.JsonException)
        {
            Status = $"TMX search failed: {e.Message}";
        }
    }

    private HashSet<(string Game, long Id)> InstalledIds() =>
        _services.TracksDirectory is { } dir ? InstalledTracks.Scan(dir).Select(t => (t.Game, t.TmxId)).ToHashSet() : [];

    private void MarkInstalled()
    {
        var installed = InstalledIds();
        foreach (var item in Results)
        {
            item.IsInstalled = installed.Contains((item.Track.Game.Id(), item.Track.Id));
        }
    }
}

/// <summary>One search result.</summary>
internal sealed partial class TrackItemViewModel(TmxTrack track, IReadOnlyDictionary<int, string> tagNames) : ObservableObject
{
    public TmxTrack Track { get; } = track;
    public string Name { get; } = TmText.StripFormatting(track.Name);
    public string Author { get; } = TmText.StripFormatting(track.Authors.Count > 0 ? track.Authors[0] : track.Uploader);
    public string AuthorTime { get; } = Format.Time(track.AuthorTimeMs);
    public int Awards => Track.Awards;
    public string Type { get; } = TmxEnums.PrimaryType(track.PrimaryType);
    public IReadOnlyList<string> Tags { get; } = [.. track.TagIds.Select(id => tagNames.TryGetValue(id, out var name) ? name : $"#{id}")];

    /// <summary>Only Stadium converts today (TMUF-X also has the other environments).</summary>
    public bool IsConvertible => Track.Game == TmGame.Tmnf || Track.Environment == 7;

    [ObservableProperty]
    public partial bool IsInstalled { get; set; }

    [ObservableProperty]
    public partial BitmapSource? Thumbnail { get; set; }

    public async Task LoadThumbnailAsync(TmxClient tmx, CancellationToken cancellationToken) =>
        Thumbnail = await Images.LoadTmxAsync(tmx, Track.Game, Track.Id, 0, 200, cancellationToken).ConfigureAwait(true);
}
