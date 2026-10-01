using System.Numerics;

namespace Tm2Ac.AcTrack;

/// <summary>One point of an AC AI spline, in AC space.</summary>
/// <param name="Speed">Target speed in m/s.</param>
/// <param name="Gas">Throttle hint 0–1.</param>
/// <param name="Brake">Brake hint 0–1.</param>
/// <param name="Radius">Turn radius in metres (large = straight).</param>
/// <param name="SideLeft">Distance to the left track edge, metres.</param>
/// <param name="SideRight">Distance to the right track edge, metres.</param>
public sealed record AiPoint(Vector3 Position, float Speed, float Gas, float Brake, float Radius, float SideLeft, float SideRight)
{
    public Vector3 Normal { get; init; } = Vector3.UnitY;
    public Vector3 Forward { get; init; } = Vector3.UnitZ;

    /// <summary>Turn direction: +1 or −1 (sign of the curvature), as in Kunos files.</summary>
    public float Direction { get; init; } = 1;
}

/// <summary>
/// ai/fast_lane.ai, version 7 (docs/research/S6-fast-lane-ai.md): header, points (position, cumulative length, id),
/// 18 floats of extra data per point, then a spatial grid flag. We write flag 0 (no grid), as 19 installed community
/// tracks do (fn_nurburgring, dousojin_touge, ...); AC builds the grid itself.
/// </summary>
public static class FastLaneAi
{
    public const int Version = 7;

    public static void Write(IReadOnlyList<AiPoint> points, string path)
    {
        using var stream = File.Create(path);
        Write(points, stream);
    }

    public static void Write(IReadOnlyList<AiPoint> points, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(points);
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(Version);
        w.Write(points.Count);
        w.Write(0); // lap time
        w.Write(0); // sample count

        var lengths = new float[points.Count];
        for (var i = 1; i < points.Count; i++)
        {
            lengths[i] = lengths[i - 1] + Vector3.Distance(points[i - 1].Position, points[i].Position);
        }

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i].Position;
            w.Write(p.X);
            w.Write(p.Y);
            w.Write(p.Z);
            w.Write(lengths[i]);
            w.Write(i);
        }

        w.Write(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var segment = i + 1 < points.Count ? Vector3.Distance(p.Position, points[i + 1].Position) : 0;
            w.Write(p.Speed);
            w.Write(p.Gas);
            w.Write(p.Brake);
            w.Write(0f); // obsolete lateral G
            w.Write(p.Radius);
            w.Write(p.SideLeft);
            w.Write(p.SideRight);
            w.Write(0f); // camber
            w.Write(p.Direction);
            w.Write(p.Normal.X);
            w.Write(p.Normal.Y);
            w.Write(p.Normal.Z);
            w.Write(segment);
            w.Write(p.Forward.X);
            w.Write(p.Forward.Y);
            w.Write(p.Forward.Z);
            w.Write(0f); // tag
            w.Write(0f); // grade
        }

        w.Write(0); // no spatial grid
    }

    /// <summary>Reads positions and the main extras (for tests and inspection).</summary>
    public static IReadOnlyList<AiPoint> Read(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var version = r.ReadInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported fast_lane.ai version {version}.");
        }

        var count = r.ReadInt32();
        r.ReadInt32();
        r.ReadInt32();
        var positions = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            positions[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            r.ReadSingle();
            r.ReadInt32();
        }

        var extras = r.ReadInt32();
        var points = new List<AiPoint>(count);
        for (var i = 0; i < extras; i++)
        {
            var f = new float[18];
            for (var k = 0; k < 18; k++)
            {
                f[k] = r.ReadSingle();
            }

            points.Add(new AiPoint(positions[i], f[0], f[1], f[2], f[4], f[5], f[6])
            {
                Direction = f[8],
                Normal = new Vector3(f[9], f[10], f[11]),
                Forward = new Vector3(f[13], f[14], f[15]),
            });
        }

        return points;
    }
}
