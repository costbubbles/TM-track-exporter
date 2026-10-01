using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tm2Ac.Gbx;

/// <summary>
/// Per-block metadata from data/block-flags.&lt;game&gt;.json (embedded). The TMNF table was generated from the
/// <c>WayPointType</c> of every block info in Stadium.pak, so it's authoritative rather than name-guessing.
/// </summary>
public sealed class BlockFlagTable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    private readonly Dictionary<string, BlockFlags> _blocks;

    private BlockFlagTable(Dictionary<string, BlockFlags> blocks) => _blocks = blocks;

    public static BlockFlagTable Tmnf { get; } = LoadEmbedded("block-flags.tmnf.json");

    public int Count => _blocks.Count;

    public TmWaypoint Waypoint(string blockName) => _blocks.TryGetValue(blockName, out var flags) ? flags.Waypoint : TmWaypoint.None;

    /// <summary>Look-alike block to use when this block's geometry can't be extracted, if one is configured.</summary>
    public string? Fallback(string blockName) => _blocks.TryGetValue(blockName, out var flags) ? flags.Fallback : null;

    public static BlockFlagTable Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, Json) ?? throw new InvalidDataException("Empty block flag table.");
        return new BlockFlagTable(new Dictionary<string, BlockFlags>(file.Blocks, StringComparer.Ordinal));
    }

    private static BlockFlagTable LoadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private sealed record FileDto(Dictionary<string, BlockFlags> Blocks);
}

public sealed record BlockFlags
{
    public TmWaypoint Waypoint { get; init; }
    public string? Fallback { get; init; }
}
