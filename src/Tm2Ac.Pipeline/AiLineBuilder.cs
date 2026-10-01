using System.Numerics;
using Tm2Ac.AcTrack;
using Tm2Ac.Gbx;
using Tm2Ac.Geometry;

namespace Tm2Ac.Pipeline;

/// <summary>
/// Builds the AC AI line from a TM ghost (SPEC §7.5): one flying lap for circuits (line to line), the whole run for
/// A-to-B, resampled every 1.5 m, smoothed, snapped to the collision surface, with speeds recomputed for AC grip from
/// curvature plus braking/acceleration limits (TM speeds are far higher than any AC car's).
/// </summary>
public static class AiLineBuilder
{
    private const float Spacing = 1.5f;
    private const float Grip = 1.4f;           // lateral μ assumed for speed targets
    private const float Gravity = 9.81f;
    private const float MaxSpeed = 85f;         // m/s
    private const float MinSpeed = 8f;
    private const float BrakeDecel = 10f;       // m/s²
    private const float Accel = 6f;             // m/s²
    private const float CarHeight = 1f;         // ghost samples are car-centre positions
    private const float DefaultSide = 8f;       // half road width estimate (TMNF roads are ~16 m wide)

    public static IReadOnlyList<AiPoint>? Build(TmGhost ghost, RaceLayout layout, IReadOnlyList<int> lapLineTimes, TmnfSceneBuilder scene, IssueList issues)
    {
        ArgumentNullException.ThrowIfNull(ghost);
        ArgumentNullException.ThrowIfNull(lapLineTimes);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(issues);

        var closed = layout == RaceLayout.Circuit;
        var (from, to) = (0, ghost.RaceTimeMs);
        if (closed)
        {
            if (lapLineTimes.Count >= 2)
            {
                (from, to) = (lapLineTimes[0], lapLineTimes[1]); // a flying lap, starting on the line
            }
            else if (lapLineTimes.Count == 1)
            {
                (from, to) = (0, lapLineTimes[0]);
            }
        }

        var raw = ghost.Samples.Where(s => s.TimeMs >= from && s.TimeMs <= to).Select(s => s.Position).ToList();
        if (raw.Count < 4)
        {
            issues.Add(IssueSeverity.Warn, "AI_LINE_TOO_SHORT", "", "The replay is too short to build an AI line");
            return null;
        }

        var tm = Smooth(Resample(raw, Spacing), closed, window: 3);

        // Snap to the drivable surface under the car; airborne stretches just drop by the car height.
        var airborne = 0;
        for (var i = 0; i < tm.Count; i++)
        {
            var p = tm[i];
            if (scene.SurfaceHeightBelow(p + new Vector3(0, 0.5f, 0), 4f) is { } y)
            {
                tm[i] = p with { Y = y };
            }
            else
            {
                tm[i] = p with { Y = p.Y - CarHeight };
                airborne++;
            }
        }

        if (airborne > tm.Count / 10)
        {
            issues.Add(IssueSeverity.Warn, "AI_LINE_AIRBORNE", "", $"{airborne * 100 / tm.Count}% of the AI line has no surface under it (jumps); AI may struggle there");
        }

        var ac = tm.Select(scene.ToAc).ToList();
        return Annotate(ac, closed, scene.Scale);
    }

    /// <summary>Points at even arc-length spacing along the polyline.</summary>
    public static List<Vector3> Resample(IReadOnlyList<Vector3> points, float spacing)
    {
        ArgumentNullException.ThrowIfNull(points);
        var result = new List<Vector3> { points[0] };
        var carry = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var length = Vector3.Distance(a, b);
            var d = spacing - carry;
            while (d <= length)
            {
                result.Add(Vector3.Lerp(a, b, d / length));
                d += spacing;
            }

            carry = length - (d - spacing);
        }

        return result;
    }

    /// <summary>Moving average over ±<paramref name="window"/> points (wrapping on closed lines, clamped on open ones).</summary>
    public static List<Vector3> Smooth(IReadOnlyList<Vector3> points, bool closed, int window)
    {
        ArgumentNullException.ThrowIfNull(points);
        var n = points.Count;
        var result = new List<Vector3>(n);
        for (var i = 0; i < n; i++)
        {
            var sum = Vector3.Zero;
            var count = 0;
            for (var k = -window; k <= window; k++)
            {
                var j = i + k;
                if (closed)
                {
                    j = ((j % n) + n) % n;
                }
                else if (j < 0 || j >= n)
                {
                    continue;
                }

                sum += points[j];
                count++;
            }

            result.Add(sum / count);
        }

        return result;
    }

    /// <summary>Adds forward vectors, turn radius/direction, speed targets and gas/brake hints.</summary>
    public static IReadOnlyList<AiPoint> Annotate(IReadOnlyList<Vector3> points, bool closed, float scale)
    {
        ArgumentNullException.ThrowIfNull(points);
        var n = points.Count;
        Vector3 At(int i) => closed ? points[((i % n) + n) % n] : points[Math.Clamp(i, 0, n - 1)];
        const int span = 5; // ~7.5 m either side for curvature

        var radius = new float[n];
        var direction = new float[n];
        var forward = new Vector3[n];
        var speed = new float[n];
        for (var i = 0; i < n; i++)
        {
            var a = At(i - span);
            var b = At(i);
            var c = At(i + span);
            var f = At(i + 1) - At(i - 1);
            forward[i] = f.LengthSquared() > 0 ? Vector3.Normalize(f) : Vector3.UnitZ;

            var a2 = new Vector2(a.X, a.Z);
            var b2 = new Vector2(b.X, b.Z);
            var c2 = new Vector2(c.X, c.Z);
            var cross = ((b2.X - a2.X) * (c2.Y - a2.Y)) - ((b2.Y - a2.Y) * (c2.X - a2.X));
            var area = MathF.Abs(cross) / 2;
            radius[i] = area < 1e-3f ? 10_000 : Vector2.Distance(a2, b2) * Vector2.Distance(b2, c2) * Vector2.Distance(c2, a2) / (4 * area);
            direction[i] = cross >= 0 ? 1 : -1;
            speed[i] = Math.Clamp(MathF.Sqrt(Grip * Gravity * radius[i]), MinSpeed, MaxSpeed * MathF.Sqrt(scale));
        }

        // Braking (backward) then acceleration (forward) limits; closed lines wrap, so run twice.
        for (var pass = 0; pass < (closed ? 2 : 1); pass++)
        {
            for (var i = n - 2 + (closed ? 1 : 0); i >= 0; i--)
            {
                var next = (i + 1) % n;
                var ds = Vector3.Distance(points[i], points[next]);
                speed[i] = MathF.Min(speed[i], MathF.Sqrt((speed[next] * speed[next]) + (2 * BrakeDecel * ds)));
            }
        }

        var limited = (float[])speed.Clone();
        for (var pass = 0; pass < (closed ? 2 : 1); pass++)
        {
            for (var i = 1; i < n + (closed ? 1 : 0); i++)
            {
                var cur = i % n;
                var prev = i - 1;
                var ds = Vector3.Distance(points[prev], points[cur]);
                limited[cur] = MathF.Min(limited[cur], MathF.Sqrt((limited[prev] * limited[prev]) + (2 * Accel * ds)));
            }
        }

        var result = new List<AiPoint>(n);
        for (var i = 0; i < n; i++)
        {
            var next = closed ? (i + 1) % n : Math.Min(i + 1, n - 1);
            var braking = limited[next] < limited[i] - 0.2f;
            result.Add(new AiPoint(points[i], limited[i], braking ? 0 : 1, braking ? 1 : 0, radius[i], DefaultSide * scale, DefaultSide * scale)
            {
                Forward = forward[i],
                Direction = direction[i],
                Normal = AcAxes.Up,
            });
        }

        return result;
    }
}
