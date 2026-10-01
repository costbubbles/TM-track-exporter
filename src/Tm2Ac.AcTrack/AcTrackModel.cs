using System.Numerics;
using Tm2Ac.Geometry;

namespace Tm2Ac.AcTrack;

/// <summary>
/// Everything needed to write one AC track folder (single layout). Geometry is in AC space.
/// Produced by the synthetic test-track builder now, and by the TM → AC pipeline later.
/// </summary>
public sealed class AcTrackModel
{
    /// <summary>Folder name under content/tracks, also used for the visual KN5 file name.</summary>
    public required string Id { get; init; }

    public required UiTrackInfo Ui { get; init; }

    public List<AcTexture> Textures { get; } = [];
    public List<AcMaterial> Materials { get; } = [];
    public List<AcVisualMesh> VisualMeshes { get; } = [];
    public List<AcCollisionMesh> CollisionMeshes { get; } = [];
    public List<AcDummy> Dummies { get; } = [];
    public List<SurfaceDefinition> Surfaces { get; } = [];

    /// <summary>Enables CSP extended physics in surfaces.ini (needed for <see cref="SurfaceDefinition.ExtSurfaceType"/>).</summary>
    public bool ExtendedPhysics { get; init; }

    public LightingSettings Lighting { get; init; } = new();

    /// <summary>The driving line drawn into map.png and outline.png.</summary>
    public required Centerline MapPath { get; init; }

    /// <summary>Optional ui/preview.png contents; a generated image is used when null.</summary>
    public byte[]? PreviewPng { get; init; }
}

public sealed record AcTexture(string Name, byte[] Data);

/// <summary>A <c>ksPerPixel</c>-family material with a diffuse texture.</summary>
public sealed record AcMaterial(string Name, string DiffuseTexture)
{
    public string Shader { get; init; } = "ksPerPixel";
    public bool AlphaTested { get; init; }
    public float Ambient { get; init; } = 0.4f;
    public float Diffuse { get; init; } = 0.6f;
    public float Specular { get; init; } = 0.1f;
    public float SpecularExponent { get; init; } = 10f;
    public float Emissive { get; init; }
}

public sealed record AcVisualMesh(string Name, string Material, MeshData Mesh)
{
    public bool CastShadows { get; init; } = true;
}

/// <summary>Physics geometry. Its KN5 node name is derived from <see cref="SurfaceKey"/> (see <see cref="AcNames.CollisionMesh"/>).</summary>
public sealed record AcCollisionMesh(string SurfaceKey, MeshData Mesh);

/// <summary>An <c>AC_*</c> marker. <see cref="Forward"/> becomes the dummy's local +Z (race direction for spawns and gates).</summary>
public sealed record AcDummy(string Name, Vector3 Position, Vector3 Forward);

public sealed record LightingSettings(float SunPitchAngle = 45, float SunHeadingAngle = 0);

public sealed record UiTrackInfo(string Name)
{
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string Country { get; init; } = "";
    public string City { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Url { get; init; } = "";
    public int? Year { get; init; }

    /// <summary>"clockwise", "anticlockwise" or "a2b"; empty to omit.</summary>
    public string Run { get; init; } = "";
}

/// <summary>Names AC looks for. Dummy conventions are documented in docs/research/S5-conventions.md.</summary>
public static class AcNames
{
    public static string Start(int index) => $"AC_START_{index}";

    public static string Pit(int index) => $"AC_PIT_{index}";

    public const string HotlapStart = "AC_HOTLAP_START_0";

    /// <summary>Timing gate n (0 = start/finish line). Returns the left and right dummy names.</summary>
    public static (string Left, string Right) TimeGate(int index) => ($"AC_TIME_{index}_L", $"AC_TIME_{index}_R");

    public static readonly (string Left, string Right) AbStart = ("AC_AB_START_L", "AC_AB_START_R");

    public static readonly (string Left, string Right) AbFinish = ("AC_AB_FINISH_L", "AC_AB_FINISH_R");

    /// <summary>
    /// Collision node name in CSP's documented scheme <c>&lt;digits&gt;&lt;KEY&gt;&lt;digits&gt;</c>, e.g. <c>1ROAD0003</c>.
    /// A non-digit right after the key would make AC/CSP misread the surface.
    /// </summary>
    public static string CollisionMesh(string surfaceKey, int index) => $"1{surfaceKey}{index:D4}";
}

public static class AcDummies
{
    /// <summary>Gate dummies at <paramref name="center"/>: _L at <c>center + left·halfWidth</c>, _R opposite, both facing the race direction.</summary>
    public static IEnumerable<AcDummy> Gate((string Left, string Right) names, Vector3 center, Vector3 forward, float halfWidth)
    {
        var left = AcAxes.Left(forward);
        yield return new AcDummy(names.Left, center + (left * halfWidth), forward);
        yield return new AcDummy(names.Right, center - (left * halfWidth), forward);
    }
}
