using System.Numerics;
using GBX.NET;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Plug;
using GBX.NET.Engines.Scene;
using Tm2Ac.Gbx;
using Tm2Ac.Geometry;

namespace Tm2Ac.Assets;

/// <summary>
/// Extracts block geometry from a TMNF install: per variant (ground/air × index) the visual LOD-0 meshes grouped by
/// material, and the collision mesh grouped by physics surface. Coordinates are TM block-local metres (a 1×1 block
/// spans x, z ∈ [0, 32]); placement and TM→AC conversion happen later (Phase 4).
/// </summary>
public sealed class TmnfBlockExtractor(TmnfPakFileSystem fs)
{
    // Maps reference blocks by the info's Ident.Id, which can differ from the file name
    // (e.g. StadiumRoadMainStartLine lives in StadiumRoadMainStart.TMEDClassic.Gbx), so index by Ident.
    private readonly Dictionary<string, string> _blockPaths = IndexBlockInfos(fs);

    private readonly Dictionary<string, ExtractedMaterial> _materials = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> BlockNames => _blockPaths.Keys;

    /// <summary>Materials seen so far, keyed by game path.</summary>
    public IReadOnlyDictionary<string, ExtractedMaterial> Materials => _materials;

    public ExtractedBlock Extract(string blockName)
    {
        if (!_blockPaths.TryGetValue(blockName, out var path))
        {
            return new ExtractedBlock(blockName) { Warnings = [$"Block info '{blockName}' not found in the paks."] };
        }

        var warnings = new List<string>();
        var info = fs.OpenNode<CGameCtnBlockInfo>(path);
        if (info is null)
        {
            return new ExtractedBlock(blockName) { Warnings = [$"Could not open block info {path}."] };
        }

        var variants = new List<ExtractedVariant>();
        AddVariants(variants, info.GroundMobils, isGround: true, warnings);
        AddVariants(variants, info.AirMobils, isGround: false, warnings);

        return new ExtractedBlock(blockName)
        {
            InfoClass = info.GetType().Name,
            GroundUnits = Units(info.GroundBlockUnitInfos),
            AirUnits = Units(info.AirBlockUnitInfos),
            SpawnGround = Spawn(info.SpawnLocGround),
            SpawnAir = Spawn(info.SpawnLocAir),
            Variants = variants,
            Warnings = warnings,
        };
    }

    /// <summary>Spawn transform in block-local space: position plus the local Z axis (column 3 of the Iso4).</summary>
    private static BlockSpawn? Spawn(Iso4? location) =>
        location is { } m && (m.XX != 0 || m.YY != 0 || m.ZZ != 0)
            ? new BlockSpawn(new Vector3(m.TX, m.TY, m.TZ), Vector3.Normalize(new Vector3(m.XZ, m.YZ, m.ZZ)))
            : null;

    private static List<GridCoord> Units(CGameCtnBlockUnitInfo[]? units) =>
        units?.Select(u => new GridCoord(u.RelativeOffset.X, u.RelativeOffset.Y, u.RelativeOffset.Z)).ToList() ?? [];

    private void AddVariants(List<ExtractedVariant> variants, External<CSceneMobil>[][]? mobils, bool isGround, List<string> warnings)
    {
        if (mobils is null)
        {
            return;
        }

        for (var index = 0; index < mobils.Length; index++)
        {
            var variant = new ExtractedVariant(isGround, index);
            foreach (var external in mobils[index] ?? [])
            {
                var mobil = Safe(() => external.Node, warnings, $"mobil {external.File?.FilePath}") ?? fs.OpenNode<CSceneMobil>(fs.ResolveReference(external.File) ?? "");
                if (mobil is null)
                {
                    warnings.Add($"{(isGround ? "ground" : "air")}[{index}]: mobil {external.File?.FilePath} not found");
                    continue;
                }

                var solid = Safe(() => ResolveSolid(mobil.Item?.Solid), warnings, "solid");
                if (solid is null)
                {
                    continue; // mobils without geometry exist (e.g. pure sound/trigger mobils), or the solid failed to parse
                }

                Safe(() => AddVisuals(variant, solid, warnings), warnings, $"visuals of {mobil.Item?.Solid?.TreeFile?.FilePath}");
                Safe(() => AddCollision(variant, solid, warnings), warnings, $"collision of {mobil.Item?.Solid?.TreeFile?.FilePath}");
            }

            variants.Add(variant);
        }
    }

    /// <summary>A mobil's solid is often a stub whose Tree points at the real .Solid.Gbx (itself a CPlugSolid).</summary>
    private static CPlugSolid? ResolveSolid(CPlugSolid? solid)
    {
        for (var depth = 0; solid is not null && depth < 4; depth++)
        {
            if (solid.Tree is CPlugSolid inner)
            {
                solid = inner;
                continue;
            }

            return solid.Tree is CPlugTree ? solid : null;
        }

        return null;
    }

    private void AddVisuals(ExtractedVariant variant, CPlugSolid solid, List<string> warnings)
    {
        foreach (var (tree, location) in solid.GetAllChildrenWithLocation(lod: 0))
        {
            if (tree.Visual is not CPlugVisualIndexedTriangles visual || visual.IndexBuffer?.Indices is not { Length: > 0 } indices)
            {
                continue;
            }

            var materialPath = fs.ResolveReference(tree.MaterialFile) ?? tree.MaterialFile?.FilePath ?? "(inline)";
            if (Safe(() => tree.Material, warnings, $"material {materialPath}") is { } material)
            {
                RegisterMaterial(materialPath, material, warnings);
            }

            var mesh = new MeshData();
            var uvs = visual.TexCoords is { Length: > 0 } sets ? sets[0].TexCoords : null;
            var hasNormals = visual.Vertices.Length > 0 && visual.Vertices[0].Normal is not null;
            var rotation = Rotation(location);
            for (var i = 0; i < visual.Vertices.Length; i++)
            {
                var v = visual.Vertices[i];
                var normal = hasNormals && v.Normal is { } n ? Vector3.Normalize(Vector3.TransformNormal(new Vector3(n.X, n.Y, n.Z), rotation)) : Vector3.UnitY;
                // TM's V axis runs bottom-up; AC (DirectX) samples top-down, so flip V (verified on road and sign textures, S2).
                var uv = uvs is not null && i < uvs.Length ? new Vector2(uvs[i].UV.X, 1 - uvs[i].UV.Y) : Vector2.Zero;
                mesh.AddVertex(Transform(location, v.Position), normal, uv);
            }

            for (var t = 0; t + 2 < indices.Length; t += 3)
            {
                mesh.AddTriangle(indices[t], indices[t + 1], indices[t + 2]);
            }

            variant.Parts.Add(new MeshPart(materialPath, mesh));
        }
    }

    private void AddCollision(ExtractedVariant variant, CPlugSolid solid, List<string> warnings)
    {
        if (solid.Tree is not CPlugTree root || root.Surface is not CPlugSurface surface || surface.Geom?.Surf is not CPlugSurface.Mesh mesh)
        {
            return;
        }

        var location = root.Location ?? Iso4.Identity;
        var surfaceIds = (surface.Materials ?? []).Select(m =>
        {
            var material = Safe(() => m.Material, warnings, "collision material");
            if (material is not null)
            {
                RegisterMaterial(fs.ResolveReference(m.MaterialFile) ?? "(inline)", material, warnings);
            }

            return material?.SurfaceId.ToString() ?? m.SurfaceId?.ToString() ?? surface.Geom?.SurfaceId.ToString() ?? "Unknown";
        }).ToList();

        foreach (var triangle in mesh.CookedTriangles ?? [])
        {
            var surfaceId = triangle.SurfaceIndex < surfaceIds.Count ? surfaceIds[triangle.SurfaceIndex] : surface.Geom?.SurfaceId.ToString() ?? "Unknown";
            if (!variant.Collision.TryGetValue(surfaceId, out var target))
            {
                variant.Collision[surfaceId] = target = new MeshData();
            }

            var a = target.Positions.Count;
            target.Positions.Add(Transform(location, mesh.Vertices[triangle.Indices.X]));
            target.Positions.Add(Transform(location, mesh.Vertices[triangle.Indices.Y]));
            target.Positions.Add(Transform(location, mesh.Vertices[triangle.Indices.Z]));
            target.AddTriangle(a, a + 1, a + 2);
        }
    }

    private void RegisterMaterial(string path, CPlugMaterial material, List<string> warnings)
    {
        if (_materials.ContainsKey(path))
        {
            return;
        }

        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var bitmap in material.CustomMaterial?.Textures ?? [])
        {
            var slot = bitmap.Name ?? $"Slot{textures.Count}";
            var texture = Safe(() => bitmap.Texture as CPlugBitmap, warnings: null, "");
            var image = texture is not null ? fs.ResolveReference(texture.ImageFile) : ImageFromTextureFile(fs.ResolveReference(bitmap.TextureFile));
            if (image is not null)
            {
                textures[slot] = image;
            }
        }

        if (textures.Count == 0)
        {
            warnings.Add($"material {MaterialName(path)} has no custom textures");
        }

        _materials[path] = new ExtractedMaterial(path, material.SurfaceId.ToString())
        {
            BaseShader = MaterialName(fs.ResolveReference(material.ShaderFile) ?? ""),
            Textures = textures,
        };
    }

    /// <summary>
    /// Fallback when a bitmap file can't be parsed: Nadeo stores <c>DirFoo.Texture.Gbx</c> next to <c>DirImageFoo.dds</c>.
    /// </summary>
    private string? ImageFromTextureFile(string? texturePath)
    {
        if (texturePath is null)
        {
            return null;
        }

        var name = Path.GetFileName(texturePath).Split('.')[0];
        var candidate = Path.Combine(Path.GetDirectoryName(texturePath) ?? "", "Image", name + ".dds");
        return fs.FindOnDisk(candidate) is not null ? candidate : null;
    }

    /// <summary>Runs a lazy GBX.NET access that may load (and fail to parse) another file; failures become warnings.</summary>
    private static T? Safe<T>(Func<T?> read, List<string>? warnings, string what)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            warnings?.Add($"{what}: {e.GetType().Name}: {e.Message}");
            return default;
        }
    }

    private static void Safe(Action action, List<string> warnings, string what) => Safe<object>(() => { action(); return null; }, warnings, what);

    private static string MaterialName(string path) => Path.GetFileName(path).Split('.')[0];

    private static string BlockNameFromPath(string path) => Path.GetFileName(path).Split('.')[0];

    private static Dictionary<string, string> IndexBlockInfos(TmnfPakFileSystem fs)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in fs.BlockInfoPaths().Order(StringComparer.Ordinal))
        {
            var name = fs.OpenNode<CGameCtnBlockInfo>(path)?.Ident?.Id;
            index.TryAdd(string.IsNullOrEmpty(name) ? BlockNameFromPath(path) : name, path);
        }

        return index;
    }

    private static Vector3 Transform(Iso4 m, Vec3 p) =>
        new((m.XX * p.X) + (m.XY * p.Y) + (m.XZ * p.Z) + m.TX, (m.YX * p.X) + (m.YY * p.Y) + (m.YZ * p.Z) + m.TY, (m.ZX * p.X) + (m.ZY * p.Y) + (m.ZZ * p.Z) + m.TZ);

    private static Matrix4x4 Rotation(Iso4 m) => new(m.XX, m.YX, m.ZX, 0, m.XY, m.YY, m.ZY, 0, m.XZ, m.YZ, m.ZZ, 0, 0, 0, 0, 1);
}

public sealed record ExtractedBlock(string Name)
{
    public string InfoClass { get; init; } = "";
    public IReadOnlyList<GridCoord> GroundUnits { get; init; } = [];
    public IReadOnlyList<GridCoord> AirUnits { get; init; } = [];
    public IReadOnlyList<ExtractedVariant> Variants { get; init; } = [];
    public BlockSpawn? SpawnGround { get; init; }
    public BlockSpawn? SpawnAir { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record ExtractedVariant(bool IsGround, int Index)
{
    public List<MeshPart> Parts { get; } = [];

    /// <summary>Collision triangles grouped by TM physics surface (e.g. "Asphalt", "Concrete", "Turbo_Deprecated").</summary>
    public Dictionary<string, MeshData> Collision { get; } = new(StringComparer.Ordinal);
}

/// <summary>Car spawn point of a start block, in block-local coordinates.</summary>
public sealed record BlockSpawn(Vector3 Position, Vector3 Forward);

public sealed record MeshPart(string MaterialPath, MeshData Mesh);

public sealed record ExtractedMaterial(string Path, string SurfaceId)
{
    public string? BaseShader { get; init; }

    /// <summary>Texture slot (Diffuse, Normal, Specular, Occlusion, ...) → game path of the image (usually a DDS under GameData).</summary>
    public IReadOnlyDictionary<string, string> Textures { get; init; } = new Dictionary<string, string>();
}
