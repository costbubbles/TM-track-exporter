using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Tm2Ac.Core;

namespace Tm2Ac.Tmx;

/// <summary>
/// Client for the TMX v2 API of TMNF-X and TMUF-X (TM2020 search uses different fields and comes in Phase 9).
/// Sends the required User-Agent, keeps at most two requests in flight, retries 429/5xx with backoff and caches on disk.
/// </summary>
public sealed class TmxClient : IDisposable
{
    public const string UserAgent = "Tm2Ac/0.1 (+https://github.com/costbubbles/TM-track-exporter)";
    public static readonly TimeSpan SearchCacheTime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MetaCacheTime = TimeSpan.FromDays(1);
    private const int MaxAttempts = 4;

    private const string TrackFields =
        "TrackId,TrackName,UId,AuthorTime,Uploader.Name,Authors[].User.Name,Tags[],Environment,Mood,Routes,Difficulty,"
        + "PrimaryType,Awards,Images[],UploadedAt,UpdatedAt,AuthorComments,WRReplay.ReplayId,WRReplay.ReplayTime";

    private const string ReplayFields = "ReplayId,ReplayTime,User.Name,IsBest";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly TmxCache _cache;
    private readonly SemaphoreSlim _concurrency = new(2);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="delay">Backoff wait; tests pass a no-op.</param>
    public TmxClient(HttpClient http, TmxCache cache, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _cache = cache;
        _delay = delay ?? Task.Delay;
    }

    private TmxClient(TmxCache cache)
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, cache)
    {
        _ownsHttp = true;
    }

    public static TmxClient CreateDefault(string? cacheDirectory = null) => new(new TmxCache(cacheDirectory ?? Tm2AcPaths.CacheDirectory));

    public TmxCache Cache => _cache;

    public async Task<TmxSearchPage> SearchTracksAsync(TmGame game, TmxSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureTrackSite(game);

        var parameters = new List<(string, string)> { ("fields", TrackFields), ("count", query.Count.ToString(CultureInfo.InvariantCulture)) };
        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            parameters.Add(("name", query.Name));
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            parameters.Add(("author", query.Author));
        }

        parameters.AddRange(query.Tags.Select(t => ("tag", t.ToString(CultureInfo.InvariantCulture))));
        parameters.AddRange(query.ExcludedTags.Select(t => ("etag", t.ToString(CultureInfo.InvariantCulture))));
        if (query.PrimaryType is { } primaryType)
        {
            parameters.Add(("primarytype", primaryType.ToString(CultureInfo.InvariantCulture)));
        }
        if (query.Order != TmxTrackOrder.Default)
        {
            parameters.Add(("order1", ((int)query.Order).ToString(CultureInfo.InvariantCulture)));
        }

        if (query.After is { } after)
        {
            parameters.Add(("after", after.ToString(CultureInfo.InvariantCulture)));
        }

        var page = await GetJsonAsync<PageDto<TrackDto>>(BuildUri(game, "api/tracks", parameters), SearchCacheTime, cancellationToken).ConfigureAwait(false);
        return new TmxSearchPage(page.Results.Select(t => t.ToModel(game)).ToList(), page.More);
    }

    /// <exception cref="TmxNotFoundException">No track with that id.</exception>
    public async Task<TmxTrack> GetTrackAsync(TmGame game, long trackId, CancellationToken cancellationToken = default)
    {
        EnsureTrackSite(game);
        var uri = BuildUri(game, "api/tracks", [("fields", TrackFields), ("id", trackId.ToString(CultureInfo.InvariantCulture))]);
        var page = await GetJsonAsync<PageDto<TrackDto>>(uri, SearchCacheTime, cancellationToken).ConfigureAwait(false);
        return page.Results.FirstOrDefault()?.ToModel(game) ?? throw new TmxNotFoundException($"{game.Id()} track {trackId} not found on {game.ExchangeBaseUri().Host}.");
    }

    /// <summary>Replays for a track, best first (TMX ordering: newest track version, then time).</summary>
    public async Task<IReadOnlyList<TmxReplay>> GetReplaysAsync(TmGame game, long trackId, int count = 10, CancellationToken cancellationToken = default)
    {
        EnsureTrackSite(game);
        var uri = BuildUri(game, "api/replays", [("trackId", trackId.ToString(CultureInfo.InvariantCulture)), ("fields", ReplayFields), ("count", count.ToString(CultureInfo.InvariantCulture))]);
        var page = await GetJsonAsync<PageDto<ReplayDto>>(uri, SearchCacheTime, cancellationToken).ConfigureAwait(false);
        return page.Results.Select(r => new TmxReplay(r.ReplayId, r.ReplayTime, r.User?.Name ?? "", r.IsBest == 1)).ToList();
    }

    /// <summary>Tag id → name for the site (cached for a day).</summary>
    public async Task<IReadOnlyDictionary<int, string>> GetTagsAsync(TmGame game, CancellationToken cancellationToken = default)
    {
        var tags = await GetJsonAsync<List<TagDto>>(new Uri(game.ExchangeBaseUri(), "api/meta/tags"), MetaCacheTime, cancellationToken).ConfigureAwait(false);
        return tags.ToDictionary(t => t.Id, t => t.Name);
    }

    /// <summary>Downloads (or reuses) the map file and returns its local path.</summary>
    public Task<string> DownloadMapAsync(TmGame game, long trackId, CancellationToken cancellationToken = default) =>
        DownloadFileAsync(new Uri(game.ExchangeBaseUri(), game == TmGame.Tm2020 ? $"mapgbx/{trackId}" : $"trackgbx/{trackId}"), _cache.MapPath(game, trackId), cancellationToken);

    public Task<string> DownloadReplayAsync(TmGame game, long replayId, CancellationToken cancellationToken = default) =>
        DownloadFileAsync(new Uri(game.ExchangeBaseUri(), $"recordgbx/{replayId}"), _cache.ReplayPath(game, replayId), cancellationToken);

    /// <summary>Track screenshot <paramref name="index"/> (TMNF-X: 0 = small image, 1..n = uploaded screenshots).</summary>
    public Task<string> DownloadImageAsync(TmGame game, long trackId, int index, CancellationToken cancellationToken = default) =>
        DownloadFileAsync(new Uri(game.ExchangeBaseUri(), game == TmGame.Tm2020 ? $"mapimage/{trackId}/{index}" : $"trackshow/{trackId}/image/{index}"), _cache.ImagePath(game, trackId, index), cancellationToken);

    public void Dispose()
    {
        _concurrency.Dispose();
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private static void EnsureTrackSite(TmGame game)
    {
        if (game == TmGame.Tm2020)
        {
            throw new NotSupportedException("TM2020 (trackmania.exchange) search is not implemented yet (Phase 9).");
        }
    }

    private static Uri BuildUri(TmGame game, string path, IEnumerable<(string Key, string Value)> parameters)
    {
        var query = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return new Uri(game.ExchangeBaseUri(), $"{path}?{query}");
    }

    private async Task<T> GetJsonAsync<T>(Uri uri, TimeSpan cacheTime, CancellationToken cancellationToken)
    {
        var cachePath = _cache.ApiPath(uri);
        var text = TmxCache.ReadFresh(cachePath, cacheTime);
        if (text is null)
        {
            var bytes = await SendAsync(uri, cancellationToken).ConfigureAwait(false);
            text = System.Text.Encoding.UTF8.GetString(bytes);
            TmxCache.Write(cachePath, text);
        }

        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new InvalidDataException($"Empty TMX response from {uri}.");
    }

    private async Task<string> DownloadFileAsync(Uri uri, string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return path;
        }

        TmxCache.Write(path, await SendAsync(uri, cancellationToken).ConfigureAwait(false));
        return path;
    }

    private async Task<byte[]> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                TimeSpan? wait;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.UserAgent.ParseAdd(UserAgent);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new TmxNotFoundException($"TMX returned 404 for {uri}.");
                    }

                    var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                    if (!retryable || attempt == MaxAttempts)
                    {
                        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        throw new HttpRequestException($"TMX request failed ({(int)response.StatusCode} {response.ReasonPhrase}) for {uri}: {Truncate(body, 300)}", null, response.StatusCode);
                    }

                    wait = response.Headers.RetryAfter?.Delta;
                }
                catch (HttpRequestException e) when (e.StatusCode is null && attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
                {
                    // Connection-level failure (no HTTP status): retry with backoff.
                    wait = null;
                }

                await _delay(wait ?? TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    private sealed record PageDto<T>(List<T> Results, bool More);

    private sealed record UserDto(string? Name);

    private sealed record AuthorDto(UserDto? User);

    private sealed record ReplayRefDto(long ReplayId, int ReplayTime);

    private sealed record ImageDto(int Width, int Height);

    private sealed record ReplayDto(long ReplayId, int ReplayTime, UserDto? User, int IsBest);

    private sealed record TagDto(int Id, string Name);

    private sealed record TrackDto(
        long TrackId,
        string? TrackName,
        string? UId,
        int? AuthorTime,
        UserDto? Uploader,
        List<AuthorDto>? Authors,
        List<int>? Tags,
        int Environment,
        int Mood,
        int Routes,
        int Difficulty,
        int PrimaryType,
        int Awards,
        List<ImageDto>? Images,
        DateTime UploadedAt,
        DateTime? UpdatedAt,
        string? AuthorComments,
        ReplayRefDto? WRReplay)
    {
        public TmxTrack ToModel(TmGame game) => new(game, TrackId, TrackName ?? "")
        {
            Uid = UId ?? "",
            Uploader = Uploader?.Name ?? "",
            Authors = Authors?.Select(a => a.User?.Name ?? "").Where(n => n.Length > 0).ToList() ?? [],
            AuthorTimeMs = AuthorTime,
            TagIds = Tags ?? [],
            Environment = Environment,
            Mood = Mood,
            Routes = Routes,
            Difficulty = Difficulty,
            PrimaryType = PrimaryType,
            Awards = Awards,
            UploadedAt = UploadedAt,
            UpdatedAt = UpdatedAt,
            Description = AuthorComments ?? "",
            WrReplayId = WRReplay?.ReplayId,
            WrTimeMs = WRReplay?.ReplayTime,
            ScreenshotCount = Images?.Count ?? 0,
        };
    }
}
