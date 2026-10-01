using System.IO;
using System.Windows.Media.Imaging;
using Tm2Ac.Assets;
using Tm2Ac.Core;
using Tm2Ac.Pipeline;
using Tm2Ac.Tmx;

namespace Tm2Ac.App.Services;

/// <summary>Composition root: the objects every page shares, created once at startup.</summary>
internal sealed class AppServices : IDisposable
{
    public AppServices(AppSettings settings, TmxClient tmx)
    {
        Settings = settings;
        Tmx = tmx;
        Conversions = new ConversionService(settings, tmx);
    }

    public AppSettings Settings { get; }
    public TmxClient Tmx { get; }
    public ConversionService Conversions { get; }

    /// <summary>Raised after a track is installed or uninstalled, so pages can refresh.</summary>
    public event EventHandler? LibraryChanged;

    public void NotifyLibraryChanged() => LibraryChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>AC's content/tracks folder, or null when AC isn't found.</summary>
    public string? TracksDirectory => Settings.ResolveAcPath() is { } ac ? AssettoCorsa.TracksDirectory(ac) : null;

    public void Dispose()
    {
        Conversions.Dispose();
        Tmx.Dispose();
    }
}

/// <summary>Runs conversions one at a time (the block library isn't thread-safe) on a worker thread.</summary>
internal sealed class ConversionService(AppSettings settings, TmxClient tmx) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TmnfBlockLibrary? _library;
    private string? _libraryRoot;

    public async Task<ConversionResult> ConvertAsync(TmGame game, long tmxId, ConversionOptions options, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var ac = settings.ResolveAcPath() ?? throw new InvalidOperationException("Assetto Corsa wasn't found. Set its folder in Settings.");
            var tmnf = settings.ResolveTmnfPath() ?? throw new InvalidOperationException("TrackMania Nations Forever wasn't found. Set its folder in Settings.");
            progress.Report("Downloading map, replay and screenshot from TMX");
            var source = await ConversionSource.FromTmxAsync(tmx, game, tmxId, null, cancellationToken).ConfigureAwait(true);
            return await Task.Run(() => new TmnfConverter(Library(tmnf, progress)).Convert(source, options, AssettoCorsa.TracksDirectory(ac), progress, cancellationToken), cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private TmnfBlockLibrary Library(string tmnfRoot, IProgress<string> progress)
    {
        if (_library is null || !string.Equals(_libraryRoot, tmnfRoot, StringComparison.OrdinalIgnoreCase))
        {
            progress.Report("Opening TMNF game files");
            _library?.Dispose();
            _library = TmnfBlockLibrary.Open(tmnfRoot);
            _libraryRoot = tmnfRoot;
        }

        return _library;
    }

    public void Dispose()
    {
        _library?.Dispose();
        _gate.Dispose();
    }
}

internal static class Images
{
    /// <summary>Decodes an image file off the UI thread into a frozen bitmap (null if it can't be read).</summary>
    public static Task<BitmapSource?> LoadAsync(string path, int decodeWidth) => Task.Run(() =>
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return (BitmapSource?)image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    });

    /// <summary>A TMX image (0 = thumbnail, 1+ = screenshots), downloaded through the cache.</summary>
    public static async Task<BitmapSource?> LoadTmxAsync(TmxClient tmx, TmGame game, long trackId, int index, int decodeWidth, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = await tmx.DownloadImageAsync(game, trackId, index, cancellationToken).ConfigureAwait(false);
            return await LoadAsync(path, decodeWidth).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TmxNotFoundException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}

internal static class Format
{
    /// <summary>Race time as m:ss.fff (or s.fff under a minute).</summary>
    public static string Time(int? ms) => ms is not { } v || v <= 0 ? "–"
        : v < 60_000 ? $"{v / 1000.0:0.000} s"
        : $"{v / 60_000}:{v % 60_000 / 1000.0:00.000}";

    public static string Size(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
    };
}
