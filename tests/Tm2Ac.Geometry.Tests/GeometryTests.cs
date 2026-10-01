using System.Numerics;

namespace Tm2Ac.Geometry.Tests;

public class AcAxesTests
{
    [Fact]
    public void LeftOfPlusZIsPlusX()
    {
        // Verified on Kunos gates: _L sits at cross(up, forward) (docs/research/S5-conventions.md).
        Assert.Equal(Vector3.UnitX, AcAxes.Left(Vector3.UnitZ));
        Assert.Equal(-Vector3.UnitX, AcAxes.Right(Vector3.UnitZ));
    }

    [Fact]
    public void DummyTransformMatchesKunosLayout()
    {
        // Magione AC_PIT dummies: row0 (-0.867, 0, 0.498), row2 (-0.498, 0, -0.867).
        var m = AcAxes.DummyTransform(new Vector3(1, 2, 3), new Vector3(-0.498f, 0, -0.867f));

        Assert.Equal(-0.867f, m.M11, 2);
        Assert.Equal(0.498f, m.M13, 2);
        Assert.Equal(1f, m.M22, 5);
        Assert.Equal(-0.498f, m.M31, 2);
        Assert.Equal(-0.867f, m.M33, 2);
        Assert.Equal(new Vector3(1, 2, 3), m.Translation);
    }
}

public class MeshDataTests
{
    [Fact]
    public void SplitKeepsEveryTriangleAndRespectsTheLimit()
    {
        var mesh = TrackMeshes.Ground(0, 1000, 0, 1000, 0, cellSize: 5, uvTile: 1); // 201 x 201 vertices
        var pieces = mesh.Split(10_000);

        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.True(p.VertexCount <= 10_000));
        Assert.Equal(mesh.TriangleCount, pieces.Sum(p => p.TriangleCount));
        Assert.All(pieces, p => Assert.Equal(p.VertexCount, p.Uvs.Count));
    }

    [Fact]
    public void SplitReturnsSameMeshWhenItFits()
    {
        var mesh = TrackMeshes.Ground(0, 10, 0, 10, 0, cellSize: 5, uvTile: 1);
        Assert.Same(mesh, Assert.Single(mesh.Split(65535)));
    }
}

public class TrackMeshesTests
{
    private static Centerline Straight() => new([new(0, 0, 0), new(0, 0, 10), new(0, 0, 20)], closed: false);

    private static IEnumerable<Vector3> FaceNormals(MeshData mesh)
    {
        for (var t = 0; t < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Positions[mesh.Indices[t]];
            var b = mesh.Positions[mesh.Indices[t + 1]];
            var c = mesh.Positions[mesh.Indices[t + 2]];
            yield return Vector3.Normalize(Vector3.Cross(b - a, c - a));
        }
    }

    [Fact]
    public void RibbonFacesUpWithKunosWinding()
    {
        var mesh = TrackMeshes.Ribbon(Straight(), 6, -6, 0, 1, 10);
        Assert.All(FaceNormals(mesh), n => Assert.True(n.Y > 0.99f));
    }

    [Fact]
    public void RibbonLeftEdgeIsOnTheLeft()
    {
        var mesh = TrackMeshes.Ribbon(Straight(), 6, -6, 0, 1, 10);
        Assert.Equal(6, mesh.Positions[0].X); // u = 0 edge, left of +Z = +X
        Assert.Equal(-6, mesh.Positions[1].X);
    }

    [Fact]
    public void GroundFacesUp()
    {
        Assert.All(FaceNormals(TrackMeshes.Ground(0, 50, 0, 50, 0, 10, 5)), n => Assert.True(n.Y > 0.99f));
    }

    [Theory]
    [InlineData(16f, -1f)]  // left wall faces right (toward the track): -X when driving +Z
    [InlineData(-16f, 1f)]  // right wall faces +X
    public void WallInnerFaceFacesTheTrack(float offset, float expectedNormalX)
    {
        var mesh = TrackMeshes.Wall(Straight(), offset, 0, 1, 8, twoSided: false);
        Assert.All(FaceNormals(mesh), n => Assert.Equal(expectedNormalX, n.X, 3));
    }
}

public class CenterlineTests
{
    private static readonly Vector2[] Square = [new(0, 0), new(0, 100), new(-100, 100), new(-100, 0)];

    [Fact]
    public void RoundedPolygonIsClosedWithExpectedLength()
    {
        const float radius = 20;
        var line = Centerline.RoundedPolygon(Square, radius, 1);

        // Perimeter 400, minus 2r per corner, plus a quarter circle per corner.
        var expected = 400 - (4 * 2 * radius) + (2 * MathF.PI * radius);
        Assert.True(line.IsClosed);
        Assert.Equal(expected, line.Length, 0.5f);
    }

    [Fact]
    public void FirstTurnFromPlusZTowardMinusXIsARightTurn()
    {
        var line = Centerline.RoundedPolygon(Square, 20, 1);
        var (_, before) = line.Sample(line.NearestDistance(new Vector3(0, 0, 50)));
        var (_, after) = line.Sample(line.NearestDistance(new Vector3(-50, 0, 100)));

        Assert.Equal(Vector3.UnitZ, before);
        Assert.Equal(-Vector3.UnitX, after);
        Assert.Equal(AcAxes.Right(before), after);
    }

    [Fact]
    public void SampleWrapsAroundClosedLines()
    {
        var line = Centerline.RoundedPolygon(Square, 20, 1);
        var (a, _) = line.Sample(10);
        var (b, _) = line.Sample(10 + line.Length);
        Assert.Equal(a.X, b.X, 3);
        Assert.Equal(a.Z, b.Z, 3);
    }
}
