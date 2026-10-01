using System.Security.Cryptography;
using System.Text;
using Tm2Ac.Core;

namespace Tm2Ac.Tmx;

/// <summary>
/// Disk cache for TMX downloads. Map, replay and image files are immutable per id and kept forever; API responses
/// expire after a caller-supplied time to live.
/// </summary>
public sealed class TmxCache(string rootDirectory)
{
    public string RootDirectory { get; } = rootDirectory;

    public string MapPath(TmGame game, long trackId) => Path.Combine(RootDirectory, "tmx", game.Id(), "maps", $"{trackId}.Gbx");

    public string ReplayPath(TmGame game, long replayId) => Path.Combine(RootDirectory, "tmx", game.Id(), "replays", $"{replayId}.Replay.Gbx");

    public string ImagePath(TmGame game, long trackId, int index) => Path.Combine(RootDirectory, "tmx", game.Id(), "images", $"{trackId}_{index}.jpg");

    public string ApiPath(Uri requestUri) =>
        Path.Combine(RootDirectory, "tmx", "api", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestUri.AbsoluteUri)))[..32] + ".json");

    /// <summary>Returns the cached text if it exists and is younger than <paramref name="timeToLive"/>.</summary>
    public static string? ReadFresh(string path, TimeSpan timeToLive)
    {
        var info = new FileInfo(path);
        return info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < timeToLive ? File.ReadAllText(path) : null;
    }

    public static void Write(string path, string text) => Write(path, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Writes atomically via a uniquely named temp file, so an interrupted download never leaves a truncated entry and
    /// concurrent writers of the same entry (parallel downloads) don't collide. If another writer wins, its copy is kept.
    /// </summary>
    public static void Write(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.part";
        File.WriteAllBytes(temp, data);
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temp);
        }
        catch (UnauthorizedAccessException) when (File.Exists(path))
        {
            File.Delete(temp);
        }
    }
}
