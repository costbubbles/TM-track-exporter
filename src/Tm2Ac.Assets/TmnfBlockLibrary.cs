namespace Tm2Ac.Assets;

/// <summary>
/// On-demand access to extracted TMNF blocks, memoised per block. Extraction of the whole Stadium set takes ~6 s, so no
/// persistent mesh cache is used (decision 2026-10-01).
/// </summary>
public sealed class TmnfBlockLibrary : IDisposable
{
    private readonly TmnfBlockExtractor _extractor;
    private readonly Dictionary<string, ExtractedBlock> _blocks = new(StringComparer.Ordinal);

    private TmnfBlockLibrary(TmnfPakFileSystem fs)
    {
        FileSystem = fs;
        _extractor = new TmnfBlockExtractor(fs);
    }

    public TmnfPakFileSystem FileSystem { get; }

    public IReadOnlyCollection<string> BlockNames => _extractor.BlockNames;

    public IReadOnlyDictionary<string, ExtractedMaterial> Materials => _extractor.Materials;

    public static TmnfBlockLibrary Open(string tmnfRoot) => new(TmnfPakFileSystem.Open(tmnfRoot));

    public ExtractedBlock Get(string blockName)
    {
        if (!_blocks.TryGetValue(blockName, out var block))
        {
            _blocks[blockName] = block = _extractor.Extract(blockName);
        }

        return block;
    }

    /// <summary>
    /// The geometry for a placed block: the variant with the requested index from the ground or air set. Falls back to
    /// variant 0 of the same set, then to the other set, when the requested one is missing or empty.
    /// </summary>
    public ExtractedVariant? GetVariant(string blockName, bool isGround, int variant)
    {
        var block = Get(blockName);
        static bool HasGeometry(ExtractedVariant v) => v.Parts.Count > 0 || v.Collision.Count > 0;

        var set = block.Variants.Where(v => v.IsGround == isGround).ToList();
        var other = block.Variants.Where(v => v.IsGround != isGround).ToList();
        return set.FirstOrDefault(v => v.Index == variant && HasGeometry(v))
            ?? set.FirstOrDefault(HasGeometry)
            ?? other.FirstOrDefault(HasGeometry);
    }

    /// <summary>Bytes of a texture referenced by a material (DDS from GameData), or null when missing.</summary>
    public byte[]? ReadTexture(string gamePath) => FileSystem.ReadFile(gamePath);

    public void Dispose() => FileSystem.Dispose();
}
