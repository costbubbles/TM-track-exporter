using System.Net;
using System.Text;
using Tm2Ac.Core;

namespace Tm2Ac.Tmx.Tests;

public sealed class TmxClientTests : IDisposable
{
    // Trimmed real response for TMNF-X #924307 (2026-09-30).
    private const string TrackJson = """
        {"More":false,"Results":[{"TrackId":924307,"TrackName":"[PF] Ph/\\ntom Fake","UId":"1VC4AjbHRzRoMxhlg8xOer0zlZa","AuthorTime":199010,
        "Uploader":{"Name":"» Ptitnono.F®"},"Authors":[{"User":{"UserId":2627,"Name":"» Ptitnono.F®"},"Role":""}],"Tags":[10],"Environment":7,
        "Mood":1,"Routes":0,"Difficulty":0,"PrimaryType":0,"Awards":1052,"Images":[{"Width":400,"Height":300,"HasHighQuality":false}],
        "UploadedAt":"2009-02-20T16:57:38","WRReplay":{"ReplayId":7325831,"ReplayTime":89250}}]}
        """;

    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "tm2ac-tmx-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();
    private readonly HttpClient _http;
    private readonly TmxClient _client;

    public TmxClientTests()
    {
        _http = new HttpClient(_handler);
        _client = new TmxClient(_http, new TmxCache(_cacheDir), delay: (_, _) => Task.CompletedTask);
    }

    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
        _handler.Dispose();
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetTrackParsesMetadataAndSendsRequiredHeadersAndFields()
    {
        _handler.Enqueue(HttpStatusCode.OK, TrackJson);

        var track = await _client.GetTrackAsync(TmGame.Tmnf, 924307, TestContext.Current.CancellationToken);

        Assert.Equal("[PF] Ph/\\ntom Fake", track.Name);
        Assert.Equal(["» Ptitnono.F®"], track.Authors);
        Assert.Equal(199010, track.AuthorTimeMs);
        Assert.Equal(7325831, track.WrReplayId);
        Assert.Equal(89250, track.WrTimeMs);
        Assert.Equal([10], track.TagIds);
        Assert.Equal(1, track.ScreenshotCount);
        Assert.Equal(new Uri("https://tmnf.exchange/trackshow/924307"), track.PageUri);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("tmnf.exchange", request.Uri.Host);
        Assert.Equal("/api/tracks", request.Uri.AbsolutePath);
        Assert.Contains("id=924307", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("fields=TrackId%2CTrackName", request.Uri.Query, StringComparison.Ordinal);
        Assert.StartsWith("Tm2Ac/", request.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownTrackThrowsNotFound()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"More":false,"Results":[]}""");
        await Assert.ThrowsAsync<TmxNotFoundException>(() => _client.GetTrackAsync(TmGame.Tmnf, 1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchResponsesAreCached()
    {
        _handler.Enqueue(HttpStatusCode.OK, TrackJson);
        var query = new TmxSearchQuery { Name = "phantom" };

        await _client.SearchTracksAsync(TmGame.Tmnf, query, TestContext.Current.CancellationToken);
        var second = await _client.SearchTracksAsync(TmGame.Tmnf, query, TestContext.Current.CancellationToken);

        Assert.Single(second.Results);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task SearchSendsSingleValuedFilters()
    {
        _handler.Enqueue(HttpStatusCode.OK, TrackJson);

        await _client.SearchTracksAsync(TmGame.Tmuf, new TmxSearchQuery { Tags = [4, 7], PrimaryType = 0, Order = TmxTrackOrder.MostAwards, Count = 5 }, TestContext.Current.CancellationToken);

        var query = Assert.Single(_handler.Requests).Uri.Query;
        Assert.Contains("tag=4&tag=7", query, StringComparison.Ordinal);
        Assert.Contains("primarytype=0", query, StringComparison.Ordinal);
        Assert.Contains("order1=6", query, StringComparison.Ordinal);
        Assert.Contains("count=5", query, StringComparison.Ordinal);
        Assert.Equal("tmuf.exchange", _handler.Requests[0].Uri.Host);
    }

    [Fact]
    public async Task RetriesTooManyRequestsAndServerErrors()
    {
        _handler.Enqueue(HttpStatusCode.TooManyRequests, "slow down");
        _handler.Enqueue(HttpStatusCode.BadGateway, "oops");
        _handler.Enqueue(HttpStatusCode.OK, TrackJson);

        var track = await _client.GetTrackAsync(TmGame.Tmnf, 924307, TestContext.Current.CancellationToken);

        Assert.Equal(924307, track.Id);
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task DoesNotRetryClientErrors()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest, """{"detail":"Invalid value for 'primarytype'"}""");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetTrackAsync(TmGame.Tmnf, 924307, TestContext.Current.CancellationToken));

        Assert.Contains("400", error.Message, StringComparison.Ordinal);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task MapDownloadsAreCachedOnDisk()
    {
        _handler.Enqueue(HttpStatusCode.OK, "GBX-fake-bytes");

        var first = await _client.DownloadMapAsync(TmGame.Tmnf, 18451, TestContext.Current.CancellationToken);
        var second = await _client.DownloadMapAsync(TmGame.Tmnf, 18451, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.Equal("GBX-fake-bytes", await File.ReadAllTextAsync(first, TestContext.Current.CancellationToken));
        Assert.Equal("/trackgbx/18451", Assert.Single(_handler.Requests).Uri.AbsolutePath);
        Assert.False(File.Exists(first + ".part"));
    }

    [Fact]
    public async Task ReplayAndImageUseExpectedEndpoints()
    {
        _handler.Enqueue(HttpStatusCode.OK, "replay");
        _handler.Enqueue(HttpStatusCode.OK, "image");

        await _client.DownloadReplayAsync(TmGame.Tmnf, 7325831, TestContext.Current.CancellationToken);
        await _client.DownloadImageAsync(TmGame.Tmnf, 924307, 1, TestContext.Current.CancellationToken);

        Assert.Equal("/recordgbx/7325831", _handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("/trackshow/924307/image/1", _handler.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Tm2020SearchIsNotSupportedYet()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => _client.SearchTracksAsync(TmGame.Tm2020, new TmxSearchQuery(), TestContext.Current.CancellationToken));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<(Uri Uri, string UserAgent)> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.UserAgent.ToString()));
            var (status, body) = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

[Trait("Category", "Network")]
public sealed class TmxLiveTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "tm2ac-tmx-live-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task FetchesReferenceMapR1()
    {
        using var client = TmxClient.CreateDefault(_cacheDir);
        var track = await client.GetTrackAsync(TmGame.Tmnf, 18451, TestContext.Current.CancellationToken);
        var map = await client.DownloadMapAsync(TmGame.Tmnf, 18451, TestContext.Current.CancellationToken);
        var replays = await client.GetReplaysAsync(TmGame.Tmnf, 18451, 3, TestContext.Current.CancellationToken);

        Assert.Equal("Always be mine", track.Name);
        Assert.Equal("GBX", Encoding.ASCII.GetString(File.ReadAllBytes(map), 0, 3));
        Assert.NotEmpty(replays);
    }
}
