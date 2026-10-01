using System.Numerics;

namespace Tm2Ac.Geometry;

/// <summary>Procedural meshes along a <see cref="Centerline"/>, wound so faces point the way AC renders them (see <see cref="AcAxes"/>).</summary>
public static class TrackMeshes
{
    /// <summary>
    /// Flat, upward-facing strip between two lateral offsets (positive = left of the race direction).
    /// UV: u runs across the strip (0 at <paramref name="leftOffset"/>, <paramref name="uAcross"/> at <paramref name="rightOffset"/>),
    /// v = distance / <paramref name="vLength"/>.
    /// </summary>
    public static MeshData Ribbon(Centerline line, float leftOffset, float rightOffset, float height, float uAcross, float vLength)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (leftOffset <= rightOffset)
        {
            throw new ArgumentException("leftOffset must be greater than rightOffset.", nameof(leftOffset));
        }

        var mesh = new MeshData();
        var rings = line.IsClosed ? line.Count + 1 : line.Count;
        for (var r = 0; r < rings; r++)
        {
            var i = r % line.Count;
            var left = AcAxes.Left(line.Forward(i));
            var p = line.Points[i] + new Vector3(0, height, 0);
            var v = (r == line.Count ? line.Length : line.Distances[i]) / vLength;
            mesh.AddVertex(p + (left * leftOffset), AcAxes.Up, new Vector2(0, v));
            mesh.AddVertex(p + (left * rightOffset), AcAxes.Up, new Vector2(uAcross, v));
        }

        for (var r = 0; r < rings - 1; r++)
        {
            int l0 = r * 2, r0 = (r * 2) + 1, l1 = (r * 2) + 2, r1 = (r * 2) + 3;
            mesh.AddQuad(l0, r0, r1, l1);
        }

        return mesh;
    }

    /// <summary>
    /// Vertical wall at a lateral <paramref name="offset"/> facing the centerline (and also facing outward when
    /// <paramref name="twoSided"/>). Texture reads left-to-right for a driver looking at it from the track:
    /// v = 0 at the top, u advances in the direction that is "rightwards" from the viewer's position.
    /// </summary>
    public static MeshData Wall(Centerline line, float offset, float bottom, float top, float uLength, bool twoSided)
    {
        ArgumentNullException.ThrowIfNull(line);
        var mesh = new MeshData();
        AddWallSide(mesh, line, offset, bottom, top, uLength, facingCenter: true);
        if (twoSided)
        {
            AddWallSide(mesh, line, offset, bottom, top, uLength, facingCenter: false);
        }

        return mesh;
    }

    private static void AddWallSide(MeshData mesh, Centerline line, float offset, float bottom, float top, float uLength, bool facingCenter)
    {
        var start = mesh.VertexCount;
        var isLeftWall = offset > 0;

        // Normal points toward the centerline for the inner face.
        var towardCenter = isLeftWall ? -1f : 1f;
        var normalSign = facingCenter ? towardCenter : -towardCenter;

        // Viewer looking at a left wall from the track sees race-forward as "right": u grows with distance.
        // Looking at a right wall, forward is "left", so u shrinks with distance. Outer faces are the mirror case.
        var uSign = (isLeftWall == facingCenter) ? 1f : -1f;

        var rings = line.IsClosed ? line.Count + 1 : line.Count;
        for (var r = 0; r < rings; r++)
        {
            var i = r % line.Count;
            var left = AcAxes.Left(line.Forward(i));
            var basePoint = line.Points[i] + (left * offset);
            var normal = left * normalSign;
            var u = uSign * (r == line.Count ? line.Length : line.Distances[i]) / uLength;
            mesh.AddVertex(basePoint + new Vector3(0, bottom, 0), normal, new Vector2(u, 1));
            mesh.AddVertex(basePoint + new Vector3(0, top, 0), normal, new Vector2(u, 0));
        }

        for (var r = 0; r < rings - 1; r++)
        {
            int b0 = start + (r * 2), t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;

            // Faces -left (toward the track for a left wall) with order b0, b1, t1, t0; the reverse faces +left.
            if (normalSign < 0)
            {
                mesh.AddQuad(b0, b1, t1, t0);
            }
            else
            {
                mesh.AddQuad(b0, t0, t1, b1);
            }
        }
    }

    /// <summary>Flat upward-facing grid of square cells covering the given XZ rectangle, UV tiled every <paramref name="uvTile"/> metres.</summary>
    public static MeshData Ground(float minX, float maxX, float minZ, float maxZ, float height, float cellSize, float uvTile)
    {
        var mesh = new MeshData();
        var nx = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / cellSize));
        var nz = Math.Max(1, (int)MathF.Ceiling((maxZ - minZ) / cellSize));
        for (var iz = 0; iz <= nz; iz++)
        {
            for (var ix = 0; ix <= nx; ix++)
            {
                var x = minX + ((maxX - minX) * ix / nx);
                var z = minZ + ((maxZ - minZ) * iz / nz);
                mesh.AddVertex(new Vector3(x, height, z), AcAxes.Up, new Vector2(x / uvTile, z / uvTile));
            }
        }

        for (var iz = 0; iz < nz; iz++)
        {
            for (var ix = 0; ix < nx; ix++)
            {
                var a = (iz * (nx + 1)) + ix;
                var b = a + 1;
                var c = a + nx + 2;
                var d = a + nx + 1;

                // a=(x0,z0) b=(x1,z0) c=(x1,z1) d=(x0,z1): cross(b-a, c-a) is -Y, so use the reverse order for an up face.
                mesh.AddQuad(a, d, c, b);
            }
        }

        return mesh;
    }
}
