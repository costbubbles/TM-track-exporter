using System.Numerics;

namespace Tm2Ac.Kn5;

/// <summary>In-memory KN5 file: textures, materials and a node tree. Layout is documented in docs/research/S1-kn5-format.md.</summary>
public sealed class Kn5Model
{
    public List<Kn5Texture> Textures { get; } = [];
    public List<Kn5Material> Materials { get; } = [];
    public required Kn5Node Root { get; set; }
}

/// <summary>Embedded texture. <paramref name="Data"/> is the raw file (DDS or PNG); AC detects the format from content.</summary>
public sealed record Kn5Texture(string Name, byte[] Data);

public enum Kn5BlendMode : byte
{
    Opaque = 0,
    AlphaBlend = 1,
    AlphaToCoverage = 2,
}

public sealed class Kn5Material
{
    public required string Name { get; init; }
    public string Shader { get; init; } = "ksPerPixel";
    public Kn5BlendMode BlendMode { get; init; }
    public bool AlphaTested { get; init; }
    public int DepthMode { get; init; }
    public List<Kn5MaterialProperty> Properties { get; } = [];
    public List<Kn5TextureSlot> TextureSlots { get; } = [];
}

/// <summary>A shader property. Most shader inputs only use <see cref="A"/>; the vector values are stored regardless.</summary>
public readonly record struct Kn5MaterialProperty(string Name, float A, Vector2 B = default, Vector3 C = default, Vector4 D = default);

public readonly record struct Kn5TextureSlot(string Name, int Slot, string TextureName);

public abstract class Kn5Node
{
    public required string Name { get; init; }
    public bool Active { get; init; } = true;
    public List<Kn5Node> Children { get; } = [];
}

/// <summary>Node class 1. <see cref="Transform"/> uses System.Numerics layout (row vectors, translation in M41..M43), which is exactly the KN5 layout.</summary>
public sealed class Kn5DummyNode : Kn5Node
{
    public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
}

/// <summary>Node class 2 (static mesh). Indices are 16-bit, so a mesh holds at most <see cref="Kn5Format.MaxVerticesPerMesh"/> vertices.</summary>
public sealed class Kn5MeshNode : Kn5Node
{
    public bool CastShadows { get; init; } = true;
    public bool IsVisible { get; init; } = true;
    public bool IsTransparent { get; init; }
    public required Kn5Vertex[] Vertices { get; init; }
    public required ushort[] Indices { get; init; }
    public int MaterialIndex { get; init; }
    public int Layer { get; init; }

    /// <summary>0/0 means no LOD limit (what community tracks use).</summary>
    public float LodIn { get; init; }
    public float LodOut { get; init; }
    public Vector3 BoundingSphereCenter { get; init; }
    public float BoundingSphereRadius { get; init; }
    public bool IsRenderable { get; init; } = true;
}

public readonly record struct Kn5Vertex(Vector3 Position, Vector3 Normal, Vector2 Uv, Vector3 Tangent);

public static class Kn5Format
{
    public const string Magic = "sc6969";
    public const int Version = 6;
    public const int MaxVerticesPerMesh = ushort.MaxValue;

    internal const int NodeClassDummy = 1;
    internal const int NodeClassMesh = 2;
    internal const int NodeClassSkinnedMesh = 3;
}
