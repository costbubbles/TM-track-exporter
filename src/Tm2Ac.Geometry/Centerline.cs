using System.Numerics;

namespace Tm2Ac.Geometry;

/// <summary>A polyline in AC space with per-point forward directions and cumulative distance. Closed lines wrap around.</summary>
public sealed class Centerline
{
    public Centerline(IReadOnlyList<Vector3> points, bool closed)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new ArgumentException("A centerline needs at least two points.", nameof(points));
        }

        Points = points;
        IsClosed = closed;

        var distances = new float[points.Count];
        for (var i = 1; i < points.Count; i++)
        {
            distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }

        Distances = distances;
        Length = distances[^1] + (closed ? Vector3.Distance(points[^1], points[0]) : 0);
    }

    public IReadOnlyList<Vector3> Points { get; }
    public bool IsClosed { get; }

    /// <summary>Distance along the line from point 0 to each point.</summary>
    public IReadOnlyList<float> Distances { get; }

    public float Length { get; }

    public int Count => Points.Count;

    /// <summary>Unit direction of travel at point <paramref name="i"/> (average of the adjacent segments).</summary>
    public Vector3 Forward(int i)
    {
        var prev = i > 0 ? Points[i - 1] : IsClosed ? Points[^1] : Points[i];
        var next = i < Points.Count - 1 ? Points[i + 1] : IsClosed ? Points[0] : Points[i];
        return Vector3.Normalize(next - prev);
    }

    /// <summary>Position and forward direction at <paramref name="distance"/> metres along the line (wrapping when closed).</summary>
    public (Vector3 Position, Vector3 Forward) Sample(float distance)
    {
        if (IsClosed)
        {
            distance %= Length;
            if (distance < 0)
            {
                distance += Length;
            }
        }
        else
        {
            distance = Math.Clamp(distance, 0, Length);
        }

        for (var i = 0; i < Points.Count; i++)
        {
            var start = Points[i];
            var isLast = i == Points.Count - 1;
            if (isLast && !IsClosed)
            {
                break;
            }

            var end = isLast ? Points[0] : Points[i + 1];
            var segmentLength = Vector3.Distance(start, end);
            var segmentStart = Distances[i];
            if (distance <= segmentStart + segmentLength || isLast)
            {
                var t = segmentLength > 0 ? (distance - segmentStart) / segmentLength : 0;
                return (Vector3.Lerp(start, end, t), Vector3.Normalize(end - start));
            }
        }

        return (Points[^1], Forward(Points.Count - 1));
    }

    /// <summary>
    /// Builds a closed horizontal centerline from polygon corners (y = 0), replacing every corner with a circular arc of
    /// <paramref name="radius"/> and sampling straights and arcs roughly every <paramref name="step"/> metres.
    /// </summary>
    public static Centerline RoundedPolygon(IReadOnlyList<Vector2> corners, float radius, float step)
    {
        ArgumentNullException.ThrowIfNull(corners);
        if (corners.Count < 3)
        {
            throw new ArgumentException("A polygon needs at least three corners.", nameof(corners));
        }

        var points = new List<Vector3>();
        var n = corners.Count;
        for (var i = 0; i < n; i++)
        {
            var prev = corners[(i - 1 + n) % n];
            var corner = corners[i];
            var next = corners[(i + 1) % n];

            var dirIn = Vector2.Normalize(corner - prev);
            var dirOut = Vector2.Normalize(next - corner);
            var turn = MathF.Acos(Math.Clamp(Vector2.Dot(dirIn, dirOut), -1, 1));
            var tangentLength = radius * MathF.Tan(turn / 2);

            var arcStart = corner - (dirIn * tangentLength);

            // Straight from the previous arc end to this arc start (previous arc end is the last point added).
            if (points.Count > 0)
            {
                AddStraight(points, ToXZ(points[^1]), arcStart, step);
            }

            // Arc around the centre that lies on the inside of the turn.
            var cross = (dirIn.X * dirOut.Y) - (dirIn.Y * dirOut.X);
            var inward = cross > 0 ? new Vector2(-dirIn.Y, dirIn.X) : new Vector2(dirIn.Y, -dirIn.X);
            var center = arcStart + (inward * radius);
            var startAngle = MathF.Atan2(arcStart.Y - center.Y, arcStart.X - center.X);
            var sweep = cross > 0 ? turn : -turn;
            var segments = Math.Max(2, (int)MathF.Ceiling(radius * turn / step));
            for (var s = 0; s <= segments; s++)
            {
                var a = startAngle + (sweep * s / segments);
                points.Add(new Vector3(center.X + (radius * MathF.Cos(a)), 0, center.Y + (radius * MathF.Sin(a))));
            }
        }

        // Close: straight from the last arc end back to the first arc start, dropping the duplicate of point 0.
        AddStraight(points, ToXZ(points[^1]), ToXZ(points[0]), step);
        points.RemoveAt(points.Count - 1);

        return new Centerline(points, closed: true);

        static Vector2 ToXZ(Vector3 p) => new(p.X, p.Z);
    }

    /// <summary>Distance along the line of the vertex nearest to <paramref name="position"/> (horizontal distance).</summary>
    public float NearestDistance(Vector3 position)
    {
        var best = 0;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < Points.Count; i++)
        {
            var d = Vector2.DistanceSquared(new Vector2(Points[i].X, Points[i].Z), new Vector2(position.X, position.Z));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return Distances[best];
    }

    private static void AddStraight(List<Vector3> points, Vector2 from, Vector2 to, float step)
    {
        var length = Vector2.Distance(from, to);
        var segments = (int)MathF.Ceiling(length / step);
        for (var s = 1; s <= segments; s++)
        {
            var p = Vector2.Lerp(from, to, (float)s / segments);
            points.Add(new Vector3(p.X, 0, p.Y));
        }
    }
}
