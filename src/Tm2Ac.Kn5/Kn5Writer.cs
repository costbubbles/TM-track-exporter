using System.Numerics;
using System.Text;

namespace Tm2Ac.Kn5;

public static class Kn5Writer
{
    public static void Write(Kn5Model model, string path)
    {
        using var stream = File.Create(path);
        Write(model, stream);
    }

    public static void Write(Kn5Model model, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        w.Write(Encoding.ASCII.GetBytes(Kn5Format.Magic));
        w.Write(Kn5Format.Version);
        w.Write(0); // v6 extra int, always 0 in observed files

        w.Write(model.Textures.Count);
        foreach (var texture in model.Textures)
        {
            w.Write(1); // active
            WriteString(w, texture.Name);
            w.Write(texture.Data.Length);
            w.Write(texture.Data);
        }

        w.Write(model.Materials.Count);
        foreach (var material in model.Materials)
        {
            WriteString(w, material.Name);
            WriteString(w, material.Shader);
            w.Write((byte)material.BlendMode);
            w.Write(material.AlphaTested);
            w.Write(material.DepthMode);

            w.Write(material.Properties.Count);
            foreach (var p in material.Properties)
            {
                WriteString(w, p.Name);
                w.Write(p.A);
                WriteVector(w, p.B);
                WriteVector(w, p.C);
                WriteVector(w, p.D);
            }

            w.Write(material.TextureSlots.Count);
            foreach (var slot in material.TextureSlots)
            {
                WriteString(w, slot.Name);
                w.Write(slot.Slot);
                WriteString(w, slot.TextureName);
            }
        }

        WriteNode(w, model.Root, model.Materials.Count);
    }

    private static void WriteNode(BinaryWriter w, Kn5Node node, int materialCount)
    {
        switch (node)
        {
            case Kn5DummyNode dummy:
                w.Write(Kn5Format.NodeClassDummy);
                WriteNodeHeader(w, node);
                WriteMatrix(w, dummy.Transform);
                break;

            case Kn5MeshNode mesh:
                if (mesh.Vertices.Length > Kn5Format.MaxVerticesPerMesh)
                {
                    throw new InvalidOperationException($"Mesh '{mesh.Name}' has {mesh.Vertices.Length} vertices; KN5 meshes are limited to {Kn5Format.MaxVerticesPerMesh}. Split it first.");
                }

                if (mesh.MaterialIndex < 0 || mesh.MaterialIndex >= materialCount)
                {
                    throw new InvalidOperationException($"Mesh '{mesh.Name}' references material {mesh.MaterialIndex}, but the model has {materialCount} materials.");
                }

                w.Write(Kn5Format.NodeClassMesh);
                WriteNodeHeader(w, node);
                w.Write(mesh.CastShadows);
                w.Write(mesh.IsVisible);
                w.Write(mesh.IsTransparent);
                w.Write(mesh.Vertices.Length);
                foreach (var v in mesh.Vertices)
                {
                    WriteVector(w, v.Position);
                    WriteVector(w, v.Normal);
                    WriteVector(w, v.Uv);
                    WriteVector(w, v.Tangent);
                }

                w.Write(mesh.Indices.Length);
                foreach (var index in mesh.Indices)
                {
                    w.Write(index);
                }

                w.Write(mesh.MaterialIndex);
                w.Write(mesh.Layer);
                w.Write(mesh.LodIn);
                w.Write(mesh.LodOut);
                WriteVector(w, mesh.BoundingSphereCenter);
                w.Write(mesh.BoundingSphereRadius);
                w.Write(mesh.IsRenderable);
                break;

            default:
                throw new NotSupportedException($"Unsupported KN5 node type {node.GetType().Name}.");
        }

        foreach (var child in node.Children)
        {
            WriteNode(w, child, materialCount);
        }
    }

    private static void WriteNodeHeader(BinaryWriter w, Kn5Node node)
    {
        WriteString(w, node.Name);
        w.Write(node.Children.Count);
        w.Write(node.Active);
    }

    private static void WriteString(BinaryWriter w, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    private static void WriteMatrix(BinaryWriter w, Matrix4x4 m)
    {
        w.Write(m.M11); w.Write(m.M12); w.Write(m.M13); w.Write(m.M14);
        w.Write(m.M21); w.Write(m.M22); w.Write(m.M23); w.Write(m.M24);
        w.Write(m.M31); w.Write(m.M32); w.Write(m.M33); w.Write(m.M34);
        w.Write(m.M41); w.Write(m.M42); w.Write(m.M43); w.Write(m.M44);
    }

    private static void WriteVector(BinaryWriter w, Vector2 v)
    {
        w.Write(v.X); w.Write(v.Y);
    }

    private static void WriteVector(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
    }

    private static void WriteVector(BinaryWriter w, Vector4 v)
    {
        w.Write(v.X); w.Write(v.Y); w.Write(v.Z); w.Write(v.W);
    }
}
