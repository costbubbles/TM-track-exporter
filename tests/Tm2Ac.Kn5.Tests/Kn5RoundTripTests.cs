using System.Numerics;
using System.Text;

namespace Tm2Ac.Kn5.Tests;

public class Kn5RoundTripTests
{
    private static Kn5Model SampleModel()
    {
        var root = new Kn5DummyNode { Name = "root" };
        var model = new Kn5Model { Root = root };
        model.Textures.Add(new Kn5Texture("tex.png", [1, 2, 3, 4, 5]));
        var material = new Kn5Material { Name = "mat", Shader = "ksPerPixelAT", BlendMode = Kn5BlendMode.AlphaBlend, AlphaTested = true, DepthMode = 2 };
        material.Properties.Add(new Kn5MaterialProperty("ksDiffuse", 0.6f, new Vector2(1, 2), new Vector3(3, 4, 5), new Vector4(6, 7, 8, 9)));
        material.TextureSlots.Add(new Kn5TextureSlot("txDiffuse", 0, "tex.png"));
        model.Materials.Add(material);

        root.Children.Add(new Kn5DummyNode { Name = "AC_START_0", Transform = Matrix4x4.CreateTranslation(1, 2, 3) });
        var mesh = new Kn5MeshNode
        {
            Name = "1ROAD0000",
            CastShadows = false,
            IsTransparent = true,
            Vertices =
            [
                new(new Vector3(0, 0, 0), Vector3.UnitY, new Vector2(0, 0), Vector3.UnitX),
                new(new Vector3(1, 0, 0), Vector3.UnitY, new Vector2(1, 0), Vector3.UnitX),
                new(new Vector3(0, 0, 1), Vector3.UnitY, new Vector2(0, 1), Vector3.UnitX),
            ],
            Indices = [0, 2, 1],
            Layer = 3,
            LodIn = 1,
            LodOut = 500,
            BoundingSphereCenter = new Vector3(0.5f, 0, 0.5f),
            BoundingSphereRadius = 0.75f,
            IsRenderable = false,
        };
        root.Children.Add(mesh);
        return model;
    }

    private static byte[] WriteToBytes(Kn5Model model)
    {
        using var stream = new MemoryStream();
        Kn5Writer.Write(model, stream);
        return stream.ToArray();
    }

    [Fact]
    public void HeaderIsV6WithZeroExtraInt()
    {
        var bytes = WriteToBytes(SampleModel());

        Assert.Equal("sc6969", Encoding.ASCII.GetString(bytes, 0, 6));
        Assert.Equal(6, BitConverter.ToInt32(bytes, 6));
        Assert.Equal(0, BitConverter.ToInt32(bytes, 10));
    }

    [Fact]
    public void RoundTripPreservesEverything()
    {
        var read = Kn5Reader.Read(new MemoryStream(WriteToBytes(SampleModel())));

        var texture = Assert.Single(read.Textures);
        Assert.Equal("tex.png", texture.Name);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, texture.Data);

        var material = Assert.Single(read.Materials);
        Assert.Equal(("mat", "ksPerPixelAT", Kn5BlendMode.AlphaBlend, true, 2), (material.Name, material.Shader, material.BlendMode, material.AlphaTested, material.DepthMode));
        Assert.Equal(new Kn5MaterialProperty("ksDiffuse", 0.6f, new Vector2(1, 2), new Vector3(3, 4, 5), new Vector4(6, 7, 8, 9)), Assert.Single(material.Properties));
        Assert.Equal(new Kn5TextureSlot("txDiffuse", 0, "tex.png"), Assert.Single(material.TextureSlots));

        var root = Assert.IsType<Kn5DummyNode>(read.Root);
        Assert.Equal(2, root.Children.Count);
        var dummy = Assert.IsType<Kn5DummyNode>(root.Children[0]);
        Assert.Equal(Matrix4x4.CreateTranslation(1, 2, 3), dummy.Transform);

        var mesh = Assert.IsType<Kn5MeshNode>(root.Children[1]);
        Assert.Equal("1ROAD0000", mesh.Name);
        Assert.False(mesh.CastShadows);
        Assert.True(mesh.IsTransparent);
        Assert.False(mesh.IsRenderable);
        Assert.Equal(3, mesh.Vertices.Length);
        Assert.Equal(new Vector3(1, 0, 0), mesh.Vertices[1].Position);
        Assert.Equal(new ushort[] { 0, 2, 1 }, mesh.Indices);
        Assert.Equal((3, 1f, 500f, 0.75f), (mesh.Layer, mesh.LodIn, mesh.LodOut, mesh.BoundingSphereRadius));
    }

    [Fact]
    public void RewritingAReadModelIsByteIdentical()
    {
        var original = WriteToBytes(SampleModel());
        var rewritten = WriteToBytes(Kn5Reader.Read(new MemoryStream(original)));

        Assert.Equal(original, rewritten);
    }

    [Fact]
    public void TranslationIsStoredInTheLastMatrixRow()
    {
        var model = new Kn5Model { Root = new Kn5DummyNode { Name = "d", Transform = Matrix4x4.CreateTranslation(7, 8, 9) } };
        var bytes = WriteToBytes(model);

        // header(6+4+4) + texture count + material count + class + name("d") + child count + active byte
        var matrixStart = 14 + 4 + 4 + 4 + 4 + 1 + 4 + 1;
        Assert.Equal(7f, BitConverter.ToSingle(bytes, matrixStart + (12 * 4)));
        Assert.Equal(8f, BitConverter.ToSingle(bytes, matrixStart + (13 * 4)));
        Assert.Equal(9f, BitConverter.ToSingle(bytes, matrixStart + (14 * 4)));
    }

    [Fact]
    public void MeshOverVertexLimitIsRejected()
    {
        var model = new Kn5Model { Root = new Kn5DummyNode { Name = "root" } };
        model.Materials.Add(new Kn5Material { Name = "m" });
        model.Root.Children.Add(new Kn5MeshNode { Name = "big", Vertices = new Kn5Vertex[Kn5Format.MaxVerticesPerMesh + 1], Indices = [] });

        var error = Assert.Throws<InvalidOperationException>(() => WriteToBytes(model));
        Assert.Contains("Split it first", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Assets")]
    public void ReadsARealKunosEraDummyFile()
    {
        const string path = @"F:\SteamLibrary\steamapps\common\assettocorsa\content\tracks\ek_sadamine\idas_uphill.kn5";
        Assert.SkipUnless(File.Exists(path), "Needs the local AC install with ek_sadamine.");

        var model = Kn5Reader.Read(path);
        var names = model.Root.Children.Select(c => c.Name).ToList();

        Assert.Contains("AC_AB_START_L", names);
        Assert.Contains("AC_HOTLAP_START_0", names);
    }
}
