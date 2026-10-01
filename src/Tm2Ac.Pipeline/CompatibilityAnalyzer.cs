using Tm2Ac.Gbx;

namespace Tm2Ac.Pipeline;

public enum CompatibilityRating
{
    /// <summary>Should drive normally in AC (surfaces may be approximated).</summary>
    Green,

    /// <summary>Drivable with caveats: boosters that don't boost, big jumps, approximated special surfaces, missing pieces.</summary>
    Yellow,

    /// <summary>The route needs things AC can't do: driving upside down, on walls, or very long flights.</summary>
    Red,
}

/// <summary>What the ghost did, in milliseconds.</summary>
public sealed record GhostStats(int UpsideDownMs, int WallDrivingMs, int AirborneMs, int LongestAirMs, int BoosterContactMs, int RaceTimeMs)
{
    public int AirbornePercent => RaceTimeMs <= 0 ? 0 : AirborneMs * 100 / RaceTimeMs;
}

/// <summary>
/// Rates how well a map will work in AC (SPEC §8). The ghost is the main evidence (what the route actually requires);
/// block flags only warn when there's no ghost. Thresholds were calibrated on the 7 reference maps (2026-10-01):
/// loop maps show ≥ 500 ms upside down, normal maps 0; the longest normal jump was 2.5 s.
/// </summary>
public static class CompatibilityAnalyzer
{
    public const int UpsideDownRedMs = 300;
    public const int WallDrivingRedMs = 1000;
    public const int WallDrivingYellowMs = 300;
    public const int LongestAirRedMs = 4000;
    public const int LongestAirYellowMs = 2000;
    public const int AirbornePercentYellow = 30;
    public const int BoosterYellowMs = 1000;

    /// <summary>Wheels touching while the car's up axis is below this (≈55°) counts as driving on a wall.</summary>
    private const float WallUpY = 0.57f;
    private const float UpsideDownUpY = -0.2f;

    public static GhostStats Measure(TmGhost ghost)
    {
        ArgumentNullException.ThrowIfNull(ghost);
        int upside = 0, wall = 0, air = 0, longest = 0, run = 0, boost = 0;
        foreach (var s in ghost.Samples)
        {
            if (s.WheelsOnGround >= 1 && s.Up.Y < UpsideDownUpY)
            {
                upside++;
            }
            else if (s.WheelsOnGround >= 2 && s.Up.Y < WallUpY)
            {
                wall++;
            }

            if (s.WheelsOnGround == 0)
            {
                air++;
                longest = Math.Max(longest, ++run);
            }
            else
            {
                run = 0;
            }

            if (s.Surface.StartsWith("Turbo", StringComparison.Ordinal))
            {
                boost++;
            }
        }

        var p = ghost.SamplePeriodMs;
        return new GhostStats(upside * p, wall * p, air * p, longest * p, boost * p, ghost.RaceTimeMs);
    }

    /// <summary>Adds compatibility issues for the map/ghost to <paramref name="issues"/> and returns the ghost stats (if any).</summary>
    public static GhostStats? Analyze(TmMap map, TmGhost? ghost, IssueList issues)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(issues);

        var featureBlocks = map.Blocks
            .Select(b => (Block: b, Feature: BlockFlagTable.Tmnf.Feature(b.Name)))
            .Where(x => x.Feature is not null)
            .GroupBy(x => x.Feature!)
            .ToDictionary(g => g.Key, g => g.Count());

        if (ghost is null)
        {
            foreach (var (feature, count) in featureBlocks)
            {
                issues.Add(IssueSeverity.Warn, $"{feature.ToUpperInvariant()}_BLOCK", feature,
                    $"The map has {count} {feature.ToLowerInvariant()} block(s); without a replay it can't be checked whether the route uses them");
            }

            return null;
        }

        var stats = Measure(ghost);
        if (stats.UpsideDownMs >= UpsideDownRedMs)
        {
            issues.Add(IssueSeverity.Block, "UPSIDE_DOWN", "", $"The route drives upside down for {stats.UpsideDownMs / 1000.0:0.0} s (loops); AC cars can't");
        }

        if (stats.WallDrivingMs >= WallDrivingRedMs)
        {
            issues.Add(IssueSeverity.Block, "WALL_DRIVING", "", $"The route drives on walls for {stats.WallDrivingMs / 1000.0:0.0} s; AC cars can't");
        }
        else if (stats.WallDrivingMs >= WallDrivingYellowMs)
        {
            issues.Add(IssueSeverity.Warn, "WALL_DRIVING", "", $"The route briefly drives on steep walls ({stats.WallDrivingMs / 1000.0:0.0} s)");
        }

        if (stats.LongestAirMs >= LongestAirRedMs)
        {
            issues.Add(IssueSeverity.Block, "LONG_AIRTIME", "", $"The route has a {stats.LongestAirMs / 1000.0:0.0} s flight; AC cars won't make that jump");
        }
        else if (stats.LongestAirMs >= LongestAirYellowMs || stats.AirbornePercent >= AirbornePercentYellow)
        {
            issues.Add(IssueSeverity.Warn, "LONG_AIRTIME", "", $"Big jumps: longest flight {stats.LongestAirMs / 1000.0:0.0} s, airborne {stats.AirbornePercent}% of the run; may need a larger --scale or may be impossible");
        }

        if (stats.BoosterContactMs > 0)
        {
            issues.Add(stats.BoosterContactMs >= BoosterYellowMs ? IssueSeverity.Warn : IssueSeverity.Info, "BOOSTER_UNSUPPORTED", "ghost", $"The route uses boosters ({stats.BoosterContactMs / 1000.0:0.0} s on turbo pads); they don't boost in AC");
        }

        if (featureBlocks.Count > 0 && stats.UpsideDownMs < UpsideDownRedMs && stats.WallDrivingMs < WallDrivingRedMs)
        {
            issues.Add(IssueSeverity.Info, "FEATURE_BLOCKS_UNUSED", "", $"The map has {string.Join(", ", featureBlocks.Select(kv => $"{kv.Value} {kv.Key.ToLowerInvariant()}"))} block(s), but the replay doesn't use them");
        }

        return stats;
    }

    public static CompatibilityRating Rate(IEnumerable<ConversionIssue> issues)
    {
        var list = issues.ToList();
        return list.Any(i => i.Severity == IssueSeverity.Block) ? CompatibilityRating.Red
            : list.Any(i => i.Severity == IssueSeverity.Warn) ? CompatibilityRating.Yellow
            : CompatibilityRating.Green;
    }
}
