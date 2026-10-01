using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.MwFoundations;
using GBX.NET.PAK;
using Tm2Ac.Gbx;

namespace Tm2Ac.Assets;

/// <summary>
/// Read-only view of a TMNF install: every decryptable <c>Packs/*.pak</c> plus loose files under <c>GameData</c>.
/// Paths use the game's own layout (e.g. <c>Stadium\Media\Solid\Road\Main\Turbo\Ground.Solid.Gbx</c>).
/// Gbx files are opened with their reference tables wired up recursively, so lazy properties such as
/// <c>CPlugSolid.Tree</c> or <c>SurfMaterial.Material</c> load across files at any depth (docs/research/S2-tmnf-assets.md).
/// Not thread-safe.
/// </summary>
public sealed class TmnfPakFileSystem : IDisposable
{
    private static readonly PropertyInfo RefTableProperty =
        typeof(GbxRefTableNode).GetProperty("RefTable", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("GBX.NET changed: GbxRefTableNode.RefTable not found.");

    private readonly List<(Pak Pak, Stream Stream)> _paks = [];
    private readonly Dictionary<string, (Pak Pak, PakFile File)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GBX.NET.Gbx?> _gbxCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GbxRefTable, string> _refTableBase = new(ReferenceEqualityComparer.Instance);

    private TmnfPakFileSystem(string root)
    {
        Root = root;
        GameDataDirectory = Path.Combine(root, "GameData");
    }

    public string Root { get; }
    public string GameDataDirectory { get; }

    /// <summary>Identifies the installed game data; extracted assets are cached per build key.</summary>
    public string BuildKey { get; private set; } = "";

    /// <summary>Files that exist but failed to parse, with the error (for reports).</summary>
    public Dictionary<string, string> FailedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static TmnfPakFileSystem Open(string tmnfRoot)
    {
        GbxSetup.EnsureInitialized();
        var packs = Path.Combine(tmnfRoot, "Packs");
        var packList = PakList.Parse(Path.Combine(packs, "packlist.dat"), PakListGame.TM);
        var fs = new TmnfPakFileSystem(tmnfRoot);
        var keySource = new StringBuilder();

        foreach (var pakPath in Directory.GetFiles(packs, "*.pak").Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(pakPath).ToLowerInvariant();
            if (!packList.TryGetValue(name, out var item))
            {
                continue;
            }

            var stream = File.OpenRead(pakPath);
            var pak = Pak.Parse(stream, item.GetBytes(), KeyType.BaseKey);
            fs._paks.Add((pak, stream));
            foreach (var (key, file) in pak.Files)
            {
                fs._files.TryAdd(key, (pak, file));
            }

            var info = new FileInfo(pakPath);
            keySource.Append(name).Append(':').Append(info.Length).Append(';');
        }

        if (fs._paks.Count == 0)
        {
            throw new InvalidDataException($"No readable .pak files in {packs}.");
        }

        fs.BuildKey = "tmnf-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySource.ToString())))[..12].ToLowerInvariant();
        return fs;
    }

    /// <summary>Paths of all block info files (any CGameCtnBlockInfo subclass) in the paks.</summary>
    public IEnumerable<string> BlockInfoPaths() =>
        _files.Keys.Where(k => k.Contains("ConstructionBlockInfo", StringComparison.OrdinalIgnoreCase) && k.EndsWith(".Gbx", StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a pak entry by real path, falling back to the game's hashed names (MD5 of progressively longer path suffixes).</summary>
    public bool TryFindPakEntry(string path, out (Pak Pak, PakFile File) entry)
    {
        path = NormalizePath(path);
        if (_files.TryGetValue(path, out entry))
        {
            return true;
        }

        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        while (!string.IsNullOrEmpty(directory))
        {
            if (_files.TryGetValue(directory + "\\" + GBX.NET.Crypto.MD5.Compute136(name), out entry))
            {
                return true;
            }

            name = Path.GetFileName(directory) + "\\" + name;
            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    /// <summary>The loose file under GameData for <paramref name="path"/>, if present (TMNF keeps its DDS textures there).</summary>
    public string? FindOnDisk(string path)
    {
        var full = Path.Combine(GameDataDirectory, NormalizePath(path));
        return File.Exists(full) ? full : null;
    }

    /// <summary>Raw bytes of a file from disk (preferred) or the paks; null when missing.</summary>
    public byte[]? ReadFile(string path)
    {
        var disk = FindOnDisk(path);
        if (disk is not null)
        {
            return File.ReadAllBytes(disk);
        }

        if (!TryFindPakEntry(path, out var entry))
        {
            return null;
        }

        using var stream = entry.Pak.OpenFile(entry.File, out _);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public T? OpenNode<T>(string path) where T : CMwNod => OpenGbx(path)?.Node as T;

    /// <summary>Opens (and caches) a Gbx from the paks or disk, registering its references for lazy loading.</summary>
    public GBX.NET.Gbx? OpenGbx(string path)
    {
        path = NormalizePath(path);
        if (_gbxCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        _gbxCache[path] = null; // guards against reference cycles
        GBX.NET.Gbx? gbx = null;
        try
        {
            if (TryFindPakEntry(path, out var entry))
            {
                gbx = entry.Pak.OpenGbxFile(entry.File, new GbxReadSettings(), importExternalNodesFromRefTable: false, fileHashes: null!);
            }
            else if (FindOnDisk(path) is { } disk)
            {
                gbx = GBX.NET.Gbx.Parse(disk);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Some files use chunks GBX.NET can't parse (seen on a few CPlugBitmap/CPlugSolid files). Callers treat this as missing.
            FailedFiles[path] = $"{e.GetType().Name}: {e.Message}";
            return null;
        }

        if (gbx?.RefTable is { } refTable)
        {
            var baseDirectory = Ancestor(Path.GetDirectoryName(path) ?? "", refTable.AncestorLevel);
            _refTableBase[refTable] = baseDirectory;
            foreach (var file in refTable.Files)
            {
                var target = NormalizePath(Path.Combine(baseDirectory, file.FilePath));
                refTable.ExternalNodes[file.FilePath] = () => OpenGbx(target)!;
            }
        }

        _gbxCache[path] = gbx;
        return gbx;
    }

    /// <summary>Resolves a reference (e.g. a bitmap's <c>ImageFile</c>) to a game path, using the ref table it came from.</summary>
    public string? ResolveReference(GbxRefTableFile? file)
    {
        if (file is null)
        {
            return null;
        }

        var refTable = (GbxRefTable)RefTableProperty.GetValue(file)!;
        return _refTableBase.TryGetValue(refTable, out var baseDirectory) ? NormalizePath(Path.Combine(baseDirectory, file.FilePath)) : null;
    }

    public void Dispose()
    {
        foreach (var (pak, stream) in _paks)
        {
            pak.Dispose();
            stream.Dispose();
        }

        _paks.Clear();
    }

    /// <summary>Backslash separators, no "." / ".." segments, no leading separator.</summary>
    public static string NormalizePath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else if (part != ".")
            {
                parts.Add(part);
            }
        }

        return string.Join('\\', parts);
    }

    private static string Ancestor(string directory, int levels)
    {
        for (var i = 0; i < levels; i++)
        {
            directory = Path.GetDirectoryName(directory) ?? "";
        }

        return directory;
    }
}
