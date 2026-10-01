using System.Numerics;
using Tm2Ac.AcTrack;
using Tm2Ac.Assets;
using Tm2Ac.Gbx;
using Tm2Ac.Geometry;

namespace Tm2Ac.Pipeline;

/// <summary>
/// Assembles a TMNF map into AC geometry: every placed block's visual parts (batched per material and 128 m chunk) and
/// collision (per AC surface and chunk), the default grass field and edge walls. Positions are converted to AC space by
/// <see cref="ToAc"/>: the map is centred on the origin, the default ground level (TM y = 9) becomes y = 0, then scaled.
/// TM and AC share handedness and winding, so no mirroring or index flips are needed (S5).
/// </summary>
public sealed class TmnfSceneBuilder
{
    public const float GroundLevel = 9f;
    private const float ChunkSize = 128f;
    private const string GrassBlock = "StadiumGrass";

    /// <summary>The grass tile's flat ground quad; its blade decoration (~1,200 triangles per cell) is left out of the fill.</summary>
    private const string GrassGroundMaterial = "StadiumGrass9m";
    private const float GrassFillDrop = 0.05f;

    private readonly TmnfBlockLibrary _library;
    private readonly SurfaceMap _surfaces;
    private readonly MaterialTranslator _materials;
    private readonly Dictionary<(string Material, int X, int Z), MeshData> _visual = [];
    private readonly Dictionary<(string Key, int X, int Z), MeshData> _collision = [];
    private readonly Dictionary<(string Key, int X, int Z), Dictionary<Vector3, int>> _collisionWeld = [];
    private readonly IssueList _issues;
    private Vector3 _center;
    private float _scale = 1;

    public TmnfSceneBuilder(TmnfBlockLibrary library, IssueList issues, SurfaceMap? surfaces = null)
    {
        _library = library;
        _issues = issues;
        _surfaces = surfaces ?? SurfaceMap.Tmnf;
        _materials = new MaterialTranslator(library.ReadTexture);
    }

    public int PlacedBlocks { get; private set; }

    public float Scale => _scale;
    public int VisualTriangles => _visual.Values.Sum(m => m.TriangleCount);
    public int CollisionTriangles => _collision.Values.Sum(m => m.TriangleCount);

    /// <summary>TM world position → AC position.</summary>
    public Vector3 ToAc(Vector3 tm) => new((tm.X - _center.X) * _scale, (tm.Y - GroundLevel) * _scale, (tm.Z - _center.Z) * _scale);

    /// <summary>
    /// Height (TM space) of the highest drivable collision surface below <paramref name="tmPosition"/>, searching at most
    /// <paramref name="maxDrop"/> metres down; null if there is none. Walls don't count. Valid after <see cref="Build"/>.
    /// </summary>
    public float? SurfaceHeightBelow(Vector3 tmPosition, float maxDrop)
    {
        _surfaceIndex ??= BuildSurfaceIndex();
        var ac = ToAc(tmPosition);
        var p = new Vector2(ac.X, ac.Z);
        if (!_surfaceIndex.TryGetValue(SurfaceCell(p), out var triangles))
        {
            return null;
        }

        float? best = null;
        foreach (var (a, b, c) in triangles)
        {
            if (HeightInTriangle(p, a, b, c) is { } y && y <= ac.Y && y >= ac.Y - (maxDrop * _scale) && (best is null || y > best))
            {
                best = y;
            }
        }

        return best is { } h ? (h / _scale) + GroundLevel : null;
    }

    private const float SurfaceCellSize = 8f;
    private Dictionary<(int X, int Z), List<(Vector3 A, Vector3 B, Vector3 C)>>? _surfaceIndex;

    private (int X, int Z) SurfaceCell(Vector2 ac) => ((int)MathF.Floor(ac.X / (SurfaceCellSize * _scale)), (int)MathF.Floor(ac.Y / (SurfaceCellSize * _scale)));

    /// <summary>Drivable collision triangles (AC space) bucketed by the XZ cells their bounding boxes touch.</summary>
    private Dictionary<(int X, int Z), List<(Vector3 A, Vector3 B, Vector3 C)>> BuildSurfaceIndex()
    {
        var index = new Dictionary<(int X, int Z), List<(Vector3, Vector3, Vector3)>>();
        foreach (var ((key, _, _), mesh) in _collision)
        {
            if (key == SurfaceKeys.Wall)
            {
                continue;
            }

            for (var t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                var a = mesh.Positions[mesh.Indices[t]];
                var b = mesh.Positions[mesh.Indices[t + 1]];
                var c = mesh.Positions[mesh.Indices[t + 2]];
                var min = SurfaceCell(new Vector2(MathF.Min(a.X, MathF.Min(b.X, c.X)), MathF.Min(a.Z, MathF.Min(b.Z, c.Z))));
                var max = SurfaceCell(new Vector2(MathF.Max(a.X, MathF.Max(b.X, c.X)), MathF.Max(a.Z, MathF.Max(b.Z, c.Z))));
                for (var x = min.X; x <= max.X; x++)
                {
                    for (var z = min.Z; z <= max.Z; z++)
                    {
                        if (!index.TryGetValue((x, z), out var list))
                        {
                            index[(x, z)] = list = [];
                        }

                        list.Add((a, b, c));
                    }
                }
            }
        }

        return index;
    }

    /// <summary>Y of triangle abc at XZ point p (barycentric), or null when p is outside it.</summary>
    private static float? HeightInTriangle(Vector2 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var v0 = new Vector2(b.X - a.X, b.Z - a.Z);
        var v1 = new Vector2(c.X - a.X, c.Z - a.Z);
        var v2 = new Vector2(p.X - a.X, p.Y - a.Z);
        var den = (v0.X * v1.Y) - (v1.X * v0.Y);
        if (MathF.Abs(den) < 1e-9f)
        {
            return null;
        }

        var v = ((v2.X * v1.Y) - (v1.X * v2.Y)) / den;
        var w = ((v0.X * v2.Y) - (v2.X * v0.Y)) / den;
        var u = 1 - v - w;
        return u < -1e-4f || v < -1e-4f || w < -1e-4f ? null : (u * a.Y) + (v * b.Y) + (w * c.Y);
    }

    /// <summary>Adds the map's geometry to <paramref name="track"/> (meshes, collision, surfaces, materials, textures).</summary>
    public void Build(TmMap map, AcTrackModel track, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(options);
        _center = new Vector3(map.Size.X * BlockPlacement.BlockWidth / 2, 0, map.Size.Z * BlockPlacement.BlockWidth / 2);
        _scale = options.Scale;

        var groundCells = new HashSet<(int X, int Z)>();
        foreach (var block in map.Blocks)
        {
            var placed = PlaceBlock(block.Name, block.Coord, block.Direction, block.IsGround, block.Variant, block.SubVariant);

            // Terrain blocks (y = 0: pools, water, dirt, hills) replace the default grass; everything else gets grass
            // 5 cm below ground level so blocks with their own ground cover it and partial blocks show no holes.
            if (placed is { } result && block.IsGround && block.Coord.Y == 0)
            {
                foreach (var cell in result.Cells)
                {
                    groundCells.Add(cell);
                }
            }
        }

        if (options.DefaultGrass)
        {
            for (var x = 0; x < map.Size.X; x++)
            {
                for (var z = 0; z < map.Size.Z; z++)
                {
                    if (!groundCells.Contains((x, z)))
                    {
                        PlaceBlock(GrassBlock, new GridCoord(x, 0, z), TmDirection.North, isGround: true, variant: 0, countAsPlaced: false, onlyMaterial: GrassGroundMaterial, yOffset: -GrassFillDrop);
                    }
                }
            }
        }

        if (options.EdgeWalls)
        {
            AddEdgeWalls(map);
        }

        Emit(track);
    }

    /// <summary>Places one block; returns the grid cells it covers and whether it has ground-level surface, or null if it had no geometry.</summary>
    private (List<(int X, int Z)> Cells, bool ProvidesGround)? PlaceBlock(string name, GridCoord coord, TmDirection direction, bool isGround, int variant, int subVariant = 0, bool countAsPlaced = true, string? onlyMaterial = null, float yOffset = 0)
    {
        var source = name;
        var geometry = _library.GetVariant(name, isGround, variant, subVariant);
        if (geometry is null && BlockFlagTable.Tmnf.Fallback(name) is { } fallback)
        {
            source = fallback;
            geometry = _library.GetVariant(fallback, isGround, variant, subVariant);
            _issues.Add(IssueSeverity.Info, "ASSET_FALLBACK", name, $"{name} can't be extracted; using look-alike {fallback}");
        }

        if (geometry is null)
        {
            _issues.Add(IssueSeverity.Warn, "MISSING_ASSET", name, $"No geometry for block {name}; it is left out");
            return null;
        }

        var block = _library.Get(source);
        var units = (isGround ? block.GroundUnits : block.AirUnits) is { Count: > 0 } u ? u : block.GroundUnits.Count > 0 ? block.GroundUnits : block.AirUnits;
        var footprint = units.Count == 0 ? (X: 1, Z: 1) : (X: units.Max(x => x.X) + 1, Z: units.Max(x => x.Z) + 1);
        var placement = BlockPlacement.For(coord, direction, footprint);
        if (yOffset != 0)
        {
            placement = placement with { Origin = placement.Origin + new Vector3(0, yOffset, 0) };
        }

        foreach (var part in geometry.Parts)
        {
            if (onlyMaterial is not null && MaterialTranslator.MaterialName(part.MaterialPath) != onlyMaterial)
            {
                continue;
            }

            _library.Materials.TryGetValue(part.MaterialPath, out var tmMaterial);
            var material = _materials.Translate(part.MaterialPath, tmMaterial);
            if (material is not null)
            {
                AddVisual(material.Name, part.Mesh, placement);
            }
        }

        var providesGround = false;
        foreach (var (tmSurface, mesh) in geometry.Collision)
        {
            providesGround |= AddCollision(tmSurface, mesh, placement);
            if (geometry.Parts.Count == 0 && _materials.ForTerrain(tmSurface) is { } terrain)
            {
                AddTerrainVisual(terrain.Name, mesh, placement);
            }
        }

        if (countAsPlaced)
        {
            PlacedBlocks++;
        }

        var cells = new List<(int X, int Z)>();
        var odd = ((int)direction & 1) == 1;
        var (sx, sz) = odd ? (footprint.Z, footprint.X) : (footprint.X, footprint.Z);
        for (var x = 0; x < sx; x++)
        {
            for (var z = 0; z < sz; z++)
            {
                cells.Add((coord.X + x, coord.Z + z));
            }
        }

        return (cells, providesGround);
    }

    private void AddVisual(string material, MeshData local, BlockPlacement placement)
    {
        // Batch per material and chunk (by the part's first vertex; parts are at most a few blocks wide).
        var anchor = placement.TransformPoint(local.Positions.Count > 0 ? local.Positions[0] : Vector3.Zero);
        var key = (material, (int)MathF.Floor(anchor.X / ChunkSize), (int)MathF.Floor(anchor.Z / ChunkSize));
        if (!_visual.TryGetValue(key, out var target))
        {
            _visual[key] = target = new MeshData();
        }

        var offset = target.VertexCount;
        for (var i = 0; i < local.VertexCount; i++)
        {
            var normal = local.HasNormals ? Vector3.Normalize(placement.TransformDirection(local.Normals[i])) : Vector3.UnitY;
            target.AddVertex(ToAc(placement.TransformPoint(local.Positions[i])), normal, local.HasUvs ? local.Uvs[i] : Vector2.Zero);
        }

        foreach (var index in local.Indices)
        {
            target.Indices.Add(index + offset);
        }
    }

    /// <summary>
    /// Draws the upward-facing collision triangles of a block that has no visual mesh (TM generates such terrain at
    /// runtime), with planar world-space UVs every 16 m so neighbouring tiles line up.
    /// </summary>
    private void AddTerrainVisual(string material, MeshData local, BlockPlacement placement)
    {
        var mesh = new MeshData();
        for (var t = 0; t + 2 < local.Indices.Count; t += 3)
        {
            var a = placement.TransformPoint(local.Positions[local.Indices[t]]);
            var b = placement.TransformPoint(local.Positions[local.Indices[t + 1]]);
            var c = placement.TransformPoint(local.Positions[local.Indices[t + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() == 0 || Vector3.Normalize(normal).Y < 0.3f)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);
            var i = mesh.VertexCount;
            foreach (var p in (Vector3[])[a, b, c])
            {
                mesh.AddVertex(p, normal, new Vector2(p.X / 16, p.Z / 16));
            }

            mesh.AddTriangle(i, i + 1, i + 2);
        }

        if (mesh.TriangleCount > 0)
        {
            AddVisual(material, mesh, BlockPlacement.Identity);
        }
    }

    /// <summary>Adds a block's collision for one TM surface; returns true if any of it is flat ground at the default ground level.</summary>
    private bool AddCollision(string tmSurface, MeshData local, BlockPlacement placement)
    {
        var providesGround = false;
        string? reported = null;
        for (var t = 0; t + 2 < local.Indices.Count; t += 3)
        {
            var a = placement.TransformPoint(local.Positions[local.Indices[t]]);
            var b = placement.TransformPoint(local.Positions[local.Indices[t + 1]]);
            var c = placement.TransformPoint(local.Positions[local.Indices[t + 2]]);
            var normal = Vector3.Cross(b - a, c - a);
            var key = _surfaces.Classify(tmSurface, normal, out var issue);
            if (issue is not null && reported is null)
            {
                reported = issue;
                var mapped = _surfaces.Classify(tmSurface, Vector3.UnitY, out _);
                _issues.Add(IssueSeverity.Warn, issue, tmSurface, $"TM surface {tmSurface} is approximated in AC as {mapped ?? "not collidable"}");
            }

            if (key is null)
            {
                continue;
            }

            if (!providesGround && normal.LengthSquared() > 0 && Vector3.Normalize(normal).Y > 0.9f && MathF.Abs(a.Y - GroundLevel) < 1.5f)
            {
                providesGround = true;
            }

            AddCollisionTriangle(key, a, b, c);
        }

        return providesGround;
    }

    private void AddCollisionTriangle(string key, Vector3 a, Vector3 b, Vector3 c)
    {
        var chunk = (key, (int)MathF.Floor(a.X / ChunkSize), (int)MathF.Floor(a.Z / ChunkSize));
        if (!_collision.TryGetValue(chunk, out var target))
        {
            _collision[chunk] = target = new MeshData();
            _collisionWeld[chunk] = [];
        }

        // Weld shared vertices: collision meshes would otherwise store three vertices per triangle.
        var weld = _collisionWeld[chunk];
        int Vertex(Vector3 p)
        {
            var ac = ToAc(p);
            if (!weld.TryGetValue(ac, out var index))
            {
                weld[ac] = index = target.Positions.Count;
                target.Positions.Add(ac);
            }

            return index;
        }

        var ia = Vertex(a);
        var ib = Vertex(b);
        var ic = Vertex(c);
        if (ia != ib && ib != ic && ia != ic)
        {
            target.AddTriangle(ia, ib, ic);
        }
    }

    private void AddEdgeWalls(TmMap map)
    {
        var maxX = map.Size.X * BlockPlacement.BlockWidth;
        var maxZ = map.Size.Z * BlockPlacement.BlockWidth;
        var top = (map.Size.Y * BlockPlacement.BlockHeight) + 64;
        Vector3[] corners = [new(0, 0, 0), new(maxX, 0, 0), new(maxX, 0, maxZ), new(0, 0, maxZ)];
        for (var i = 0; i < 4; i++)
        {
            var p = corners[i];
            var q = corners[(i + 1) % 4];
            var pTop = p with { Y = top };
            var qTop = q with { Y = top };
            AddCollisionTriangle(SurfaceKeys.Wall, p, q, qTop);
            AddCollisionTriangle(SurfaceKeys.Wall, p, qTop, pTop);
        }
    }

    private void Emit(AcTrackModel track)
    {
        foreach (var ((material, x, z), mesh) in _visual.OrderBy(k => k.Key.Material, StringComparer.Ordinal).ThenBy(k => k.Key.X).ThenBy(k => k.Key.Z))
        {
            track.VisualMeshes.Add(new AcVisualMesh($"{material}_{x}_{z}", material, mesh));
        }

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ((key, _, _), mesh) in _collision.OrderBy(k => k.Key.Key, StringComparer.Ordinal).ThenBy(k => k.Key.X).ThenBy(k => k.Key.Z))
        {
            track.CollisionMeshes.Add(new AcCollisionMesh(key, mesh));
            usedKeys.Add(key);
        }

        foreach (var (key, surface) in _surfaces.AcSurfaces)
        {
            if (usedKeys.Contains(key))
            {
                track.Surfaces.Add(surface);
            }
        }

        track.Materials.AddRange(_materials.Materials);
        track.Textures.AddRange(_materials.Textures);
        foreach (var warning in _materials.Warnings)
        {
            _issues.Add(IssueSeverity.Info, "MATERIAL_FALLBACK", warning, warning);
        }
    }
}
