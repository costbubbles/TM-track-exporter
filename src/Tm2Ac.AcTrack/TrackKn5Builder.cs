using System.Numerics;
using SkiaSharp;
using Tm2Ac.Geometry;
using Tm2Ac.Kn5;

namespace Tm2Ac.AcTrack;

/// <summary>Turns an <see cref="AcTrackModel"/> into the visual KN5 (meshes + AC_* dummies) and the collision KN5.</summary>
public static class TrackKn5Builder
{
    public const string CollisionFileName = "collision.kn5";
    private const string NullTextureName = "tm2ac_null.png";

    public static Kn5Model BuildVisual(AcTrackModel track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var root = new Kn5DummyNode { Name = track.Id };
        var model = new Kn5Model { Root = root };

        foreach (var texture in track.Textures)
        {
            model.Textures.Add(new Kn5Texture(texture.Name, texture.Data));
        }

        var materialIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var material in track.Materials)
        {
            materialIndex[material.Name] = model.Materials.Count;
            model.Materials.Add(ToKn5(material));
        }

        foreach (var mesh in track.VisualMeshes)
        {
            if (!materialIndex.TryGetValue(mesh.Material, out var index))
            {
                throw new InvalidOperationException($"Visual mesh '{mesh.Name}' uses unknown material '{mesh.Material}'.");
            }

            var pieces = mesh.Mesh.Split(Kn5Format.MaxVerticesPerMesh);
            for (var i = 0; i < pieces.Count; i++)
            {
                var name = pieces.Count == 1 ? mesh.Name : $"{mesh.Name}_{i}";
                root.Children.Add(ToMeshNode(name, pieces[i], index, mesh.CastShadows, isRenderable: true));
            }
        }

        foreach (var dummy in track.Dummies)
        {
            root.Children.Add(new Kn5DummyNode { Name = dummy.Name, Transform = AcAxes.DummyTransform(dummy.Position, dummy.Forward) });
        }

        return model;
    }

    /// <summary>Non-renderable physics meshes named <c>1&lt;KEY&gt;&lt;NNNN&gt;</c>, sharing one placeholder material.</summary>
    public static Kn5Model BuildCollision(AcTrackModel track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var root = new Kn5DummyNode { Name = "collision" };
        var model = new Kn5Model { Root = root };
        model.Textures.Add(new Kn5Texture(NullTextureName, NullTexture()));
        var material = new Kn5Material { Name = "collision" };
        material.Properties.Add(new Kn5MaterialProperty("ksAmbient", 0));
        material.Properties.Add(new Kn5MaterialProperty("ksDiffuse", 0));
        material.TextureSlots.Add(new Kn5TextureSlot("txDiffuse", 0, NullTextureName));
        model.Materials.Add(material);

        var counter = 0;
        foreach (var collision in track.CollisionMeshes)
        {
            foreach (var piece in collision.Mesh.Split(Kn5Format.MaxVerticesPerMesh))
            {
                root.Children.Add(ToMeshNode(AcNames.CollisionMesh(collision.SurfaceKey, counter++), piece, 0, castShadows: false, isRenderable: false));
            }
        }

        return model;
    }

    private static Kn5Material ToKn5(AcMaterial m)
    {
        var material = new Kn5Material { Name = m.Name, Shader = m.Shader, AlphaTested = m.AlphaTested };
        material.Properties.Add(new Kn5MaterialProperty("ksAmbient", m.Ambient));
        material.Properties.Add(new Kn5MaterialProperty("ksDiffuse", m.Diffuse));
        material.Properties.Add(new Kn5MaterialProperty("ksSpecular", m.Specular));
        material.Properties.Add(new Kn5MaterialProperty("ksSpecularEXP", m.SpecularExponent));
        material.Properties.Add(new Kn5MaterialProperty("ksEmissive", m.Emissive));
        material.Properties.Add(new Kn5MaterialProperty("ksAlphaRef", 0));
        material.TextureSlots.Add(new Kn5TextureSlot("txDiffuse", 0, m.DiffuseTexture));
        return material;
    }

    private static Kn5MeshNode ToMeshNode(string name, MeshData mesh, int materialIndex, bool castShadows, bool isRenderable)
    {
        var normals = mesh.HasNormals ? mesh.Normals : FaceNormals(mesh);
        var tangents = Tangents(mesh, normals);
        var vertices = new Kn5Vertex[mesh.VertexCount];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new Kn5Vertex(mesh.Positions[i], normals[i], mesh.HasUvs ? mesh.Uvs[i] : Vector2.Zero, tangents[i]);
        }

        var (center, radius) = mesh.BoundingSphere();
        return new Kn5MeshNode
        {
            Name = name,
            CastShadows = castShadows,
            Vertices = vertices,
            Indices = mesh.Indices.Select(i => checked((ushort)i)).ToArray(),
            MaterialIndex = materialIndex,
            BoundingSphereCenter = center,
            BoundingSphereRadius = radius,
            IsRenderable = isRenderable,
        };
    }

    private static List<Vector3> FaceNormals(MeshData mesh)
    {
        var normals = Enumerable.Repeat(Vector3.Zero, mesh.VertexCount).ToList();
        for (var t = 0; t < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            var n = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
            normals[a] += n;
            normals[b] += n;
            normals[c] += n;
        }

        return normals.Select(n => n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.UnitY).ToList();
    }

    /// <summary>Per-vertex tangents from UV derivatives, orthogonalised against the normal.</summary>
    private static Vector3[] Tangents(MeshData mesh, List<Vector3> normals)
    {
        var tangents = new Vector3[mesh.VertexCount];
        if (mesh.HasUvs)
        {
            for (var t = 0; t < mesh.Indices.Count; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                var e1 = mesh.Positions[b] - mesh.Positions[a];
                var e2 = mesh.Positions[c] - mesh.Positions[a];
                var d1 = mesh.Uvs[b] - mesh.Uvs[a];
                var d2 = mesh.Uvs[c] - mesh.Uvs[a];
                var det = (d1.X * d2.Y) - (d2.X * d1.Y);
                if (MathF.Abs(det) < 1e-12f)
                {
                    continue;
                }

                var tangent = ((e1 * d2.Y) - (e2 * d1.Y)) / det;
                tangents[a] += tangent;
                tangents[b] += tangent;
                tangents[c] += tangent;
            }
        }

        for (var i = 0; i < tangents.Length; i++)
        {
            var n = normals[i];
            var tangent = tangents[i] - (n * Vector3.Dot(n, tangents[i]));
            if (tangent.LengthSquared() < 1e-12f)
            {
                tangent = Vector3.Cross(MathF.Abs(n.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX, n);
            }

            tangents[i] = Vector3.Normalize(tangent);
        }

        return tangents;
    }

    private static byte[] NullTexture()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Gray);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
