namespace Tm2Ac.Gbx;

/// <summary>A parsed Trackmania map, independent of GBX.NET types.</summary>
public sealed record TmMap
{
    public required string Uid { get; init; }

    /// <summary>Name with Trackmania formatting codes.</summary>
    public required string RawName { get; init; }

    /// <summary>Name with formatting removed.</summary>
    public required string Name { get; init; }

    public required string Author { get; init; }

    /// <summary>Environment / collection, e.g. "Stadium" (TMNF) or "Stadium2020".</summary>
    public required string Collection { get; init; }

    /// <summary>Day, Sunrise, Sunset or Night (or the raw decoration id when it can't be classified).</summary>
    public required string Mood { get; init; }

    public int? AuthorTimeMs { get; init; }
    public int? GoldTimeMs { get; init; }
    public bool IsLapRace { get; init; }
    public int Laps { get; init; }
    public required GridCoord Size { get; init; }
    public required IReadOnlyList<TmBlock> Blocks { get; init; }
    public int ItemCount { get; init; }

    /// <summary>A lap race either because the map says so or because it has a start/finish (multilap) block.</summary>
    public bool IsMultilap => IsLapRace || Blocks.Any(b => b.Waypoint == TmWaypoint.StartFinish);

    public IEnumerable<TmBlock> Waypoints => Blocks.Where(b => b.Waypoint != TmWaypoint.None);
}

/// <summary>A placed block. <see cref="Coord"/> is in block units (TMNF: 32 × 8 × 32 m), at the block's min corner.</summary>
public sealed record TmBlock(string Name, GridCoord Coord, TmDirection Direction, TmWaypoint Waypoint)
{
    public bool IsGround { get; init; }
    public int Variant { get; init; }
    public bool IsPillar { get; init; }
    public bool IsClip { get; init; }
    public int Flags { get; init; }
}

public readonly record struct GridCoord(int X, int Y, int Z)
{
    public override string ToString() => $"({X}, {Y}, {Z})";
}

/// <summary>Block facing. Verified for TMNF: South = −Z (docs/research/S5-conventions.md).</summary>
public enum TmDirection
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}

public enum TmWaypoint
{
    None,
    Start,
    Finish,
    Checkpoint,

    /// <summary>Multilap start/finish line.</summary>
    StartFinish,
}
