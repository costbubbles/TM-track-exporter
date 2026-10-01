using System.Numerics;
using Tm2Ac.Gbx;

namespace Tm2Ac.Pipeline;

/// <summary>
/// Places TMNF block-local geometry in TM world space (metres).
/// Verified facts (docs/research/S5-conventions.md, S2-tmnf-assets.md):
/// <list type="bullet">
/// <item>Block grid is 32 × 8 × 32 m; coord is the min corner of the block's footprint; no Y offset.</item>
/// <item>Block solids are modelled in [0, 32·sx] × [0, 32·sz] for an sx × sz-unit footprint.</item>
/// <item>Direction d rotates by −90°·d about Y (North keeps local +Z, East turns it to −X, South −Z, West +X), about the footprint centre.</item>
/// </list>
/// </summary>
public readonly record struct BlockPlacement(Vector3 Origin, int Direction, float FootprintX, float FootprintZ)
{
    public const float BlockWidth = 32f;
    public const float BlockHeight = 8f;

    /// <param name="footprint">Block size in units (max relative unit offset + 1) along X and Z, before rotation.</param>
    public static BlockPlacement For(GridCoord coord, TmDirection direction, (int X, int Z) footprint) =>
        new(new Vector3(coord.X * BlockWidth, coord.Y * BlockHeight, coord.Z * BlockWidth), (int)direction, footprint.X * BlockWidth, footprint.Z * BlockWidth);

    public Vector3 TransformPoint(Vector3 local)
    {
        var rotatedSize = (Direction & 1) == 1 ? new Vector2(FootprintZ, FootprintX) : new Vector2(FootprintX, FootprintZ);
        var centered = new Vector2(local.X - (FootprintX / 2), local.Z - (FootprintZ / 2));
        var rotated = Rotate(centered);
        return new Vector3(Origin.X + (rotatedSize.X / 2) + rotated.X, Origin.Y + local.Y, Origin.Z + (rotatedSize.Y / 2) + rotated.Y);
    }

    public Vector3 TransformDirection(Vector3 local)
    {
        var rotated = Rotate(new Vector2(local.X, local.Z));
        return new Vector3(rotated.X, local.Y, rotated.Y);
    }

    /// <summary>Rotation by θ = −90°·d: (x, z) → (x cosθ + z sinθ, −x sinθ + z cosθ).</summary>
    private Vector2 Rotate(Vector2 p) => (Direction & 3) switch
    {
        0 => p,
        1 => new Vector2(-p.Y, p.X),
        2 => new Vector2(-p.X, -p.Y),
        _ => new Vector2(p.Y, -p.X),
    };
}
