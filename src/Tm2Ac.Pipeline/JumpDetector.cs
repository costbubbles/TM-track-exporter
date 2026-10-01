using System.Numerics;
using Tm2Ac.Gbx;

namespace Tm2Ac.Pipeline;

/// <summary>A flight of the replay over a real gap (no drivable surface close below at some point).</summary>
/// <param name="TimeMs">Take-off time in the replay.</param>
/// <param name="Distance">Horizontal distance from take-off to the far edge of the gap, AC metres.</param>
/// <param name="Drop">How much lower the surface at the far edge is than the take-off, AC metres (negative = higher).</param>
/// <param name="RequiredKmh">Launch speed an AC car needs at the replay's launch angle; +∞ when that angle can't reach.</param>
public sealed record Jump(int TimeMs, float Distance, float Drop, float RequiredKmh);

/// <summary>
/// Finds jumps over gaps in the replay, so the user is warned that the route may need more speed than their AC car has.
/// Purely informational: maps are always converted (SPEC §8). Crests over continuous road aren't gaps (a slower car just
/// stays on the road): there the road under the flight stays above the take-off → landing chord, however high a fast car
/// flies. A flight only counts when, somewhere along it, there's no drivable surface within 10 m below that chord.
/// </summary>
public static class JumpDetector
{
    private const float Gravity = 9.81f;
    private const int MinFlightMs = 500;
    private const float GapDepth = 10f;

    /// <param name="surfaceBelow">TM-space position and search depth → surface height below (TM space), or null.</param>
    public static IReadOnlyList<Jump> Find(TmGhost ghost, Func<Vector3, float, float?> surfaceBelow, float scale)
    {
        ArgumentNullException.ThrowIfNull(ghost);
        ArgumentNullException.ThrowIfNull(surfaceBelow);
        var s = ghost.Samples;
        var jumps = new List<Jump>();
        for (var i = 0; i < s.Count; i++)
        {
            if (s[i].WheelsOnGround > 0)
            {
                continue;
            }

            var start = i;
            while (i < s.Count && s[i].WheelsOnGround == 0)
            {
                i++;
            }

            var takeoff = s[Math.Max(0, start - 1)];
            var landing = s[Math.Min(s.Count - 1, i)];
            var lastGap = Enumerable.Range(start, i - start).LastOrDefault(k => IsOverGap(s[k].Position, takeoff.Position, landing.Position, surfaceBelow), -1);
            if ((i - start) * ghost.SamplePeriodMs < MinFlightMs || lastGap < 0)
            {
                continue;
            }

            // A slower car doesn't need to fly as far as the replay did: only to the first surface past the gap.
            var farEdge = s[Math.Min(s.Count - 1, lastGap + 1)].Position;
            var edgeHeight = surfaceBelow(farEdge, farEdge.Y - landing.Position.Y + GapDepth) ?? landing.Position.Y;
            var distance = Vector2.Distance(new Vector2(takeoff.Position.X, takeoff.Position.Z), new Vector2(farEdge.X, farEdge.Z)) * scale;
            var drop = (takeoff.Position.Y - edgeHeight) * scale;
            jumps.Add(new Jump(takeoff.TimeMs, distance, drop, RequiredSpeed(takeoff.Velocity, distance, drop) * 3.6f));
        }

        return jumps;
    }

    private static bool IsOverGap(Vector3 position, Vector3 takeoff, Vector3 landing, Func<Vector3, float, float?> surfaceBelow)
    {
        var flat = static (Vector3 v) => new Vector2(v.X, v.Z);
        var span = Vector2.Distance(flat(takeoff), flat(landing));
        var t = span < 0.01f ? 0 : Math.Clamp(Vector2.Distance(flat(takeoff), flat(position)) / span, 0, 1);
        var chord = takeoff.Y + ((landing.Y - takeoff.Y) * t);
        var floor = chord - GapDepth;
        return position.Y <= floor || surfaceBelow(position, position.Y - floor) is null;
    }

    /// <summary>
    /// Launch speed (m/s) to travel <paramref name="distance"/> horizontally and land <paramref name="drop"/> lower, at the
    /// launch angle of <paramref name="takeoffVelocity"/>: v² = g·D² / (2·cos²θ·(D·tanθ + h)). +∞ when the angle can't reach.
    /// </summary>
    public static float RequiredSpeed(Vector3 takeoffVelocity, float distance, float drop)
    {
        var angle = MathF.Atan2(takeoffVelocity.Y, new Vector2(takeoffVelocity.X, takeoffVelocity.Z).Length());
        var cos = MathF.Cos(angle);
        var denominator = 2 * cos * cos * ((distance * MathF.Tan(angle)) + drop);
        return denominator <= 0 ? float.PositiveInfinity : MathF.Sqrt(Gravity * distance * distance / denominator);
    }

    /// <summary>Adds one JUMPS warning describing the hardest jump, if there are any.</summary>
    public static void Report(IReadOnlyList<Jump> jumps, IssueList issues)
    {
        ArgumentNullException.ThrowIfNull(jumps);
        ArgumentNullException.ThrowIfNull(issues);
        if (jumps.Count == 0)
        {
            return;
        }

        var hardest = jumps.MaxBy(j => j.RequiredKmh)!;
        var height = hardest.Drop >= 0 ? $"{hardest.Drop:0} m down" : $"{-hardest.Drop:0} m up";
        var speed = float.IsPositiveInfinity(hardest.RequiredKmh) ? "can't be cleared at the replay's launch angle" : $"needs about {hardest.RequiredKmh:0} km/h";
        issues.Add(IssueSeverity.Warn, "JUMPS", "",
            $"{jumps.Count} jump(s) over gaps. The hardest, at {hardest.TimeMs / 1000.0:0.0} s, is {hardest.Distance:0} m ({height}) and {speed}; slower AC cars may not make it");
    }
}
