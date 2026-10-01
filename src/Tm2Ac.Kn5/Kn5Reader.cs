using System.Numerics;
using System.Text;

namespace Tm2Ac.Kn5;

/// <summary>Reads KN5 files with static geometry (tracks). Used for round-trip tests and inspecting output; skinned meshes are rejected.</summary>
public static class Kn5Reader
{
    public static Kn5Model Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static Kn5Model Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var magic = Encoding.ASCII.GetString(r.ReadBytes(Kn5Format.Magic.Length));
        if (magic != Kn5Format.Magic)
        {
            throw new InvalidDataException($"Not a KN5 file (magic '{magic}').");
        }

        var version = r.ReadInt32();
        if (version > 5)
        {
            r.ReadInt32();
        }

        var textures = new List<Kn5Texture>();
        var textureCount = r.ReadInt32();
        for (var i = 0; i < textureCount; i++)
        {
            r.ReadInt32(); // active
            var name = ReadString(r);
            var size = r.ReadInt32();
            textures.Add(new Kn5Texture(name, r.ReadBytes(size)));
        }

        var materials = new List<Kn5Material>();
        var materialCount = r.ReadInt32();
        for (var i = 0; i < materialCount; i++)
        {
            var material = new Kn5Material
            {
                Name = ReadString(r),
                Shader = ReadString(r),
                BlendMode = (Kn5BlendMode)r.ReadByte(),
                AlphaTested = r.ReadBoolean(),
                DepthMode = r.ReadInt32(),
            };

            var propertyCount = r.ReadInt32();
            for (var p = 0; p < propertyCount; p++)
            {
                material.Properties.Add(new Kn5MaterialProperty(ReadString(r), r.ReadSingle(), ReadVector2(r), ReadVector3(r), ReadVector4(r)));
            }

            var slotCount = r.ReadInt32();
            for (var s = 0; s < slotCount; s++)
            {
                material.TextureSlots.Add(new Kn5TextureSlot(ReadString(r), r.ReadInt32(), ReadString(r)));
            }

            materials.Add(material);
        }

        var model = new Kn5Model { Root = ReadNode(r) };
        model.Textures.AddRange(textures);
        model.Materials.AddRange(materials);
        return model;
    }

    private static Kn5Node ReadNode(BinaryReader r)
    {
        var nodeClass = r.ReadInt32();
        var name = ReadString(r);
        var childCount = r.ReadInt32();
        var active = r.ReadBoolean();

        Kn5Node node = nodeClass switch
        {
            Kn5Format.NodeClassDummy => new Kn5DummyNode { Name = name, Active = active, Transform = ReadMatrix(r) },
            Kn5Format.NodeClassMesh => ReadMesh(r, name, active),
            _ => throw new NotSupportedException($"KN5 node class {nodeClass} ('{name}') is not supported."),
        };

        for (var i = 0; i < childCount; i++)
        {
            node.Children.Add(ReadNode(r));
        }

        return node;
    }

    private static Kn5MeshNode ReadMesh(BinaryReader r, string name, bool active)
    {
        var castShadows = r.ReadBoolean();
        var isVisible = r.ReadBoolean();
        var isTransparent = r.ReadBoolean();

        var vertices = new Kn5Vertex[r.ReadInt32()];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Kn5Vertex(ReadVector3(r), ReadVector3(r), ReadVector2(r), ReadVector3(r));
        }

        var indices = new ushort[r.ReadInt32()];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = r.ReadUInt16();
        }

        return new Kn5MeshNode
        {
            Name = name,
            Active = active,
            CastShadows = castShadows,
            IsVisible = isVisible,
            IsTransparent = isTransparent,
            Vertices = vertices,
            Indices = indices,
            MaterialIndex = r.ReadInt32(),
            Layer = r.ReadInt32(),
            LodIn = r.ReadSingle(),
            LodOut = r.ReadSingle(),
            BoundingSphereCenter = ReadVector3(r),
            BoundingSphereRadius = r.ReadSingle(),
            IsRenderable = r.ReadBoolean(),
        };
    }

    private static string ReadString(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));

    private static Vector2 ReadVector2(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());

    private static Vector3 ReadVector3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    private static Vector4 ReadVector4(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    private static Matrix4x4 ReadMatrix(BinaryReader r) => new(
        r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
        r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
        r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
        r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
