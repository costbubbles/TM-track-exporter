using GBX.NET;
using GBX.NET.Engines.Game;
using Tm2Ac.Core;

namespace Tm2Ac.Gbx;

/// <summary>Reads Trackmania map files (<c>*.Challenge.Gbx</c> / <c>*.Map.Gbx</c>) into <see cref="TmMap"/>.</summary>
public static class TmMapReader
{
    private static readonly string[] Moods = ["Sunrise", "Sunset", "Night", "Day"];

    static TmMapReader() => GbxSetup.EnsureInitialized();

    public static TmMap Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static TmMap Read(Stream stream)
    {
        var map = GBX.NET.Gbx.ParseNode<CGameCtnChallenge>(stream);
        var flags = BlockFlagTable.Tmnf;

        var blocks = (map.Blocks ?? []).Select(b => new TmBlock(
            b.Name,
            new GridCoord(b.Coord.X, b.Coord.Y, b.Coord.Z),
            Enum.Parse<TmDirection>(b.Direction.ToString()),
            ClassifyWaypoint(b, flags))
        {
            IsGround = b.IsGround,
            Variant = b.Variant,
            SubVariant = b.SubVariant,
            IsPillar = b.IsPillar,
            IsClip = b.IsClip,
            Flags = b.Flags,
        }).ToList();

        return new TmMap
        {
            Uid = map.MapUid ?? "",
            RawName = map.MapName ?? "",
            Name = TmText.StripFormatting(map.MapName),
            Author = TmText.StripFormatting(string.IsNullOrEmpty(map.AuthorNickname) ? map.AuthorLogin : map.AuthorNickname),
            Collection = map.Collection?.ToString() ?? "",
            Mood = MoodFromDecoration(map.Decoration?.Id),
            AuthorTimeMs = map.AuthorTime is { } time ? (int)time.TotalMilliseconds : null,
            GoldTimeMs = map.GoldTime is { } gold ? (int)gold.TotalMilliseconds : null,
            IsLapRace = map.IsLapRace,
            Laps = map.NbLaps,
            Size = new GridCoord(map.Size.X, map.Size.Y, map.Size.Z),
            Blocks = blocks,
            ItemCount = map.AnchoredObjects?.Count ?? 0,
        };
    }

    /// <summary>Extracts the mood from a decoration id: TMNF uses "Day", TM2020 uses e.g. "48x48Screen155Day".</summary>
    public static string MoodFromDecoration(string? decorationId)
    {
        if (string.IsNullOrEmpty(decorationId))
        {
            return "Day";
        }

        return Moods.FirstOrDefault(m => decorationId.EndsWith(m, StringComparison.OrdinalIgnoreCase)) ?? decorationId;
    }

    private static TmWaypoint ClassifyWaypoint(CGameCtnBlock block, BlockFlagTable flags)
    {
        var fromTable = flags.Waypoint(block.Name);
        if (fromTable != TmWaypoint.None)
        {
            return fromTable;
        }

        // TM2020 stores the waypoint role on the placed block.
        return block.WaypointSpecialProperty?.Tag switch
        {
            "Spawn" => TmWaypoint.Start,
            "Goal" => TmWaypoint.Finish,
            "Checkpoint" or "LinkedCheckpoint" => TmWaypoint.Checkpoint,
            "StartFinish" => TmWaypoint.StartFinish,
            _ => TmWaypoint.None,
        };
    }
}

/// <summary>One-time GBX.NET setup: LZO for map bodies, zlib for ghost samples and other compressed parts.</summary>
public static class GbxSetup
{
    private static readonly Lazy<bool> Initialized = new(() =>
    {
        GBX.NET.Gbx.LZO = new GBX.NET.LZO.Lzo();
        GBX.NET.Gbx.ZLib = new GBX.NET.ZLib.ZLib();
        return true;
    });

    public static void EnsureInitialized() => _ = Initialized.Value;
}
