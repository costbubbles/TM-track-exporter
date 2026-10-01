using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Tm2Ac.AcTrack.Ini;
using Tm2Ac.AcTrack.Synthetic;
using Tm2Ac.Kn5;

namespace Tm2Ac.AcTrack.Tests;

public class IniTests
{
    [Fact]
    public void NumbersUseInvariantCultureRegardlessOfCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var ini = new IniFile();
            ini.Section("S").Set("F", 0.99f).Set("V", new Vector3(1.5f, -2, 3.25f));
            Assert.Equal("[S]\r\nF=0.99\r\nV=1.5,-2,3.25\r\n", ini.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void SurfacesIniMatchesAcSystemDefaults()
    {
        var text = SurfacesIni.Build([StandardSurfaces.Road, StandardSurfaces.Grass], extendedPhysics: false).ToString();

        Assert.StartsWith("[SURFACE_0]\r\nKEY=ROAD\r\nFRICTION=1\r\nDAMPING=0\r\nWAV=\r\nWAV_PITCH=0\r\n", text, StringComparison.Ordinal);
        Assert.Contains("[SURFACE_1]\r\nKEY=GRASS\r\nFRICTION=0.6\r\n", text, StringComparison.Ordinal);
        Assert.Contains("IS_VALID_TRACK=0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtendedPhysicsMarksFirstSurfaceAndAddsExtKeys()
    {
        var ice = new SurfaceDefinition("ICE", 0.35f) { ExtSurfaceType = "ICE" };
        var text = SurfacesIni.Build([StandardSurfaces.Road, ice], extendedPhysics: true).ToString();

        Assert.Contains("WAV_PITCH=extended-0", text, StringComparison.Ordinal);
        Assert.Contains("_EXT_SURFACE_TYPE=ICE", text, StringComparison.Ordinal);
    }
}

public class MapLayoutTests
{
    [Fact]
    public void ReproducesMagioneParametersFromItsBounds()
    {
        // Magione map.ini: WIDTH=342.88 HEIGHT=861.583 MARGIN=20 X_OFFSET=187.289 Z_OFFSET=444.422 (map.png 342×861).
        // Bounds implied by those values: x -167.289..135.591, z -424.422..397.161.
        var layout = MapLayout.FromBounds(-167.289f, 135.591f, -424.422f, 397.161f, 20);

        Assert.Equal(187.289f, layout.XOffset, 3);
        Assert.Equal(444.422f, layout.ZOffset, 3);
        Assert.Equal(1f, layout.ScaleFactor);
        Assert.Equal(343, layout.Width);
        Assert.Equal(862, layout.Height);
    }

    [Fact]
    public void LargeTracksAreScaledToMaxSize()
    {
        var layout = MapLayout.FromBounds(0, 8000, 0, 2000, 20);

        Assert.True(layout.Width <= MapLayout.MaxSize);
        Assert.Equal((8000 + 40) / 1600f, layout.ScaleFactor, 3);
        Assert.Equal(new Vector2(20 / layout.ScaleFactor, 20 / layout.ScaleFactor), layout.ToPixel(Vector3.Zero));
    }
}

public class NamingTests
{
    [Theory]
    [InlineData("ROAD", 3, "1ROAD0003")]
    [InlineData("WALL", 17, "1WALL0017")]
    public void CollisionNamesFollowCspScheme(string key, int index, string expected) => Assert.Equal(expected, AcNames.CollisionMesh(key, index));

    [Fact]
    public void GateLeftDummyIsOnTheLeft()
    {
        var gate = AcDummies.Gate(AcNames.TimeGate(0), Vector3.Zero, Vector3.UnitZ, 7).ToList();
        Assert.Equal(("AC_TIME_0_L", new Vector3(7, 0, 0)), (gate[0].Name, gate[0].Position));
        Assert.Equal(("AC_TIME_0_R", new Vector3(-7, 0, 0)), (gate[1].Name, gate[1].Position));
    }
}

public sealed class TestCircuitWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tm2ac-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void WritesACompleteTrackFolder()
    {
        var track = TestCircuit.Build();
        var dir = Path.Combine(_root, track.Id);

        AcTrackWriter.Write(track, dir);

        string[] expected =
        [
            track.Id + ".kn5", "collision.kn5", "models.ini", "map.png", "README_TM2AC.txt", AcTrackWriter.ReportFileName,
            "data/surfaces.ini", "data/map.ini", "data/lighting.ini", "data/groove.ini", "data/cameras.ini",
            "ui/ui_track.json", "ui/outline.png", "ui/preview.png",
        ];
        Assert.All(expected, f => Assert.True(File.Exists(Path.Combine(dir, f)), f));

        var ui = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "ui", "ui_track.json")))!;
        Assert.Equal("8", (string?)ui["pitboxes"]);
    }

    [Fact]
    public void KnFilesContainDummiesAndHiddenCollision()
    {
        var track = TestCircuit.Build();
        var dir = Path.Combine(_root, track.Id);
        AcTrackWriter.Write(track, dir);

        var visual = Kn5Reader.Read(Path.Combine(dir, track.Id + ".kn5"));
        var names = visual.Root.Children.Select(c => c.Name).ToHashSet();
        Assert.Contains("AC_START_0", names);
        Assert.Contains("AC_PIT_7", names);
        Assert.Contains("AC_TIME_0_L", names);
        Assert.Contains("AC_TIME_2_R", names);
        Assert.Contains("AC_HOTLAP_START_0", names);

        var collision = Kn5Reader.Read(Path.Combine(dir, "collision.kn5"));
        var meshes = collision.Root.Children.Cast<Kn5MeshNode>().ToList();
        Assert.All(meshes, m => Assert.False(m.IsRenderable));
        Assert.Contains(meshes, m => m.Name.StartsWith("1ROAD", StringComparison.Ordinal));
        Assert.Contains(meshes, m => m.Name.StartsWith("1GRASS", StringComparison.Ordinal));
        Assert.Contains(meshes, m => m.Name.StartsWith("1WALL", StringComparison.Ordinal));
    }

    [Fact]
    public void SpawnsAreBehindTheStartLineFacingForward()
    {
        var track = TestCircuit.Build();
        var start = track.Dummies.Single(d => d.Name == "AC_START_0");
        var gateLeft = track.Dummies.Single(d => d.Name == "AC_TIME_0_L");
        var gateRight = track.Dummies.Single(d => d.Name == "AC_TIME_0_R");
        var gateCenter = (gateLeft.Position + gateRight.Position) / 2;

        Assert.True(Vector3.Dot(start.Position - gateCenter, start.Forward) < 0, "grid slot should be behind the line");
        Assert.Equal(Vector3.UnitZ, start.Forward); // main straight runs +Z
    }

    [Fact]
    public void RefusesToOverwriteForeignFolders()
    {
        var dir = Path.Combine(_root, "someone_elses_track");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "their.kn5"), "x");

        Assert.Throws<IOException>(() => AcTrackWriter.Write(TestCircuit.Build(), dir));
    }

    [Fact]
    public void OverwritesItsOwnOutput()
    {
        var track = TestCircuit.Build();
        var dir = Path.Combine(_root, track.Id);
        AcTrackWriter.Write(track, dir);
        AcTrackWriter.Write(track, dir);
    }
}
