using System.Numerics;

namespace Tm2Ac.Geometry;

/// <summary>
/// AC world conventions, verified against Kunos tracks (docs/research/S5-conventions.md):
/// Y is up; a dummy's local +Z is its forward; the "left" side of something facing <c>forward</c> is <c>cross(up, forward)</c>,
/// e.g. facing +Z, left is +X. Visible triangles are wound so that <c>cross(b - a, c - a)</c> points out of the front face.
/// </summary>
public static class AcAxes
{
    public static readonly Vector3 Up = Vector3.UnitY;

    public static Vector3 Left(Vector3 forward) => Vector3.Normalize(Vector3.Cross(Up, forward));

    public static Vector3 Right(Vector3 forward) => -Left(forward);

    /// <summary>
    /// Orientation + position matrix for a dummy facing <paramref name="forward"/> (projected onto the horizontal plane).
    /// Rows: 0 = local X (left), 1 = local Y (up), 2 = local Z (forward), 3 = translation.
    /// </summary>
    public static Matrix4x4 DummyTransform(Vector3 position, Vector3 forward)
    {
        var f = Vector3.Normalize(new Vector3(forward.X, 0, forward.Z));
        var x = Left(f);
        return new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            Up.X, Up.Y, Up.Z, 0,
            f.X, f.Y, f.Z, 0,
            position.X, position.Y, position.Z, 1);
    }
}
