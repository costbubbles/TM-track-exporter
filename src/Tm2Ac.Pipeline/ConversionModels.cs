namespace Tm2Ac.Pipeline;

public sealed record ConversionOptions
{
    /// <summary>Uniform world scale (SPEC §6), 0.25–4.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>
    /// Grid slots and pit boxes (SPEC §7.3). The default of 1 puts every session's spawn (race, practice/pit, hotlap) on the
    /// start block's own spawn point; more adds a staggered grid behind it for multi-car races.
    /// </summary>
    public int Pitboxes { get; init; } = 1;

    /// <summary>Fill unoccupied ground cells with the game's default grass tile.</summary>
    public bool DefaultGrass { get; init; } = true;

    /// <summary>Invisible walls at the edge of the TM map area so cars can't drive off the world.</summary>
    public bool EdgeWalls { get; init; } = true;

    /// <summary>Convert even when the map is rated Red (SPEC §8).</summary>
    public bool Force { get; init; }

    /// <summary>Force a layout instead of detecting it from the map (null = auto).</summary>
    public RaceLayout? Layout { get; init; }
}

public enum RaceLayout
{
    /// <summary>Lap race: start/finish line, sectors at checkpoints.</summary>
    Circuit,

    /// <summary>Point to point: AC "A to B" start and finish gates (time attack / hotlap).</summary>
    AToB,
}

public enum IssueSeverity
{
    Info,
    Warn,
    Block,
}

/// <summary>Something the user should know about a conversion (SPEC §8 codes, e.g. MISSING_ASSET, BOOSTER_UNSUPPORTED).</summary>
public sealed record ConversionIssue(IssueSeverity Severity, string Code, string Message);

public sealed class IssueList
{
    private readonly Dictionary<(string Code, string Key), (ConversionIssue Issue, int Count)> _issues = [];

    /// <summary>Adds an issue; repeats of the same code + key are counted instead of duplicated.</summary>
    public void Add(IssueSeverity severity, string code, string key, string message)
    {
        _issues[(code, key)] = _issues.TryGetValue((code, key), out var existing)
            ? (existing.Issue, existing.Count + 1)
            : (new ConversionIssue(severity, code, message), 1);
    }

    public IReadOnlyList<ConversionIssue> ToList() =>
        _issues.Values
            .Select(v => v.Count > 1 ? v.Issue with { Message = $"{v.Issue.Message} (x{v.Count})" } : v.Issue)
            .OrderByDescending(i => i.Severity).ThenBy(i => i.Code, StringComparer.Ordinal)
            .ToList();
}
