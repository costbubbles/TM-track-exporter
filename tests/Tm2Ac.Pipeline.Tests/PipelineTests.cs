using System.Numerics;
using Tm2Ac.AcTrack;
using Tm2Ac.Assets;
using Tm2Ac.Core;
using Tm2Ac.Gbx;
using Tm2Ac.Tmx;

namespace Tm2Ac.Pipeline.Tests;

public class BlockPlacementTests
{
    private static readonly Vector3 LocalForward = Vector3.UnitZ;

    [Theory]
    [InlineData(TmDirection.North, 0, 1)]
    [InlineData(TmDirection.East, -1, 0)]
    [InlineData(TmDirection.South, 0, -1)]
    [InlineData(TmDirection.West, 1, 0)]
    public void DirectionRotatesLocalForward(TmDirection direction, float x, float z)
    {
        var forward = BlockPlacement.For(new GridCoord(0, 0, 0), direction, (1, 1)).TransformDirection(LocalForward);
        Assert.Equal(x, forward.X, 4);
        Assert.Equal(z, forward.Z, 4);
    }

    [Theory]
    [InlineData(TmDirection.North)]
    [InlineData(TmDirection.East)]
    [InlineData(TmDirection.South)]
    [InlineData(TmDirection.West)]
    public void RotatedFootprintStaysInItsCells(TmDirection direction)
    {
        // A 2 x 1 block (64 m along local X) at coord (3, 1, 5): after rotation its corners must cover exactly its cells.
        var placement = BlockPlacement.For(new GridCoord(3, 1, 5), direction, (2, 1));
        Vector3[] corners = [new(0, 0, 0), new(64, 0, 0), new(0, 0, 32), new(64, 0, 32)];
        var placed = corners.Select(placement.TransformPoint).ToList();

        var odd = direction is TmDirection.East or TmDirection.West;
        Assert.Equal(96, placed.Min(p => p.X), 3);
        Assert.Equal(160, placed.Min(p => p.Z), 3);
        Assert.Equal(96 + (odd ? 32 : 64), placed.Max(p => p.X), 3);
        Assert.Equal(160 + (odd ? 64 : 32), placed.Max(p => p.Z), 3);
        Assert.All(placed, p => Assert.Equal(8, p.Y));
    }

    [Fact]
    public void ReproducesKnownStartSpawn()
    {
        // TMNF-X #924307: StadiumRoadMainStartLine at (16, 29, 29) facing South, spawn local (16, 2.2, 11.2) facing +Z.
        // The WR ghost's first sample is (528, 234.2, 948.8) heading -Z.
        var placement = BlockPlacement.For(new GridCoord(16, 29, 29), TmDirection.South, (1, 1));
        var spawn = placement.TransformPoint(new Vector3(16, 2.2f, 11.2f));

        Assert.Equal(528f, spawn.X, 2);
        Assert.Equal(234.2f, spawn.Y, 2);
        Assert.Equal(948.8f, spawn.Z, 2);
        Assert.Equal(-1f, placement.TransformDirection(LocalForward).Z, 4);
    }
}

public class SurfaceMapTests
{
    private static readonly SurfaceMap Map = SurfaceMap.Tmnf;

    [Theory]
    [InlineData("Asphalt", "ROAD", null)]
    [InlineData("Concrete", "ROAD", null)]
    [InlineData("Dirt", "DIRT", null)]
    [InlineData("Grass", "GRASS", null)]
    [InlineData("SlidingRubber", "ICE", "SPECIAL_SURFACE_APPROX")]
    [InlineData("Turbo_Deprecated", "ROAD", "BOOSTER_UNSUPPORTED")]
    [InlineData("SomethingNew", "ROAD", "UNKNOWN_SURFACE")]
    public void MapsFlatSurfaces(string tm, string expected, string? expectedIssue)
    {
        Assert.Equal(expected, Map.Classify(tm, Vector3.UnitY, out var issue));
        Assert.Equal(expectedIssue, issue);
    }

    [Theory]
    [InlineData("NotCollidable")]
    [InlineData("Water")]
    public void DropsNonCollidableSurfaces(string tm) => Assert.Null(Map.Classify(tm, Vector3.UnitY, out _));

    [Fact]
    public void SteepAndDownwardFacesBecomeWalls()
    {
        Assert.Equal(SurfaceKeys.Wall, Map.Classify("Asphalt", new Vector3(1, 0.2f, 0), out _));
        Assert.Equal(SurfaceKeys.Wall, Map.Classify("Asphalt", -Vector3.UnitY, out _));
        Assert.Equal("ROAD", Map.Classify("Asphalt", Vector3.Normalize(new Vector3(1, 1, 0)), out _)); // 45° ramp
    }

    [Fact]
    public void AcSurfacesDefineEverythingExceptWall()
    {
        Assert.Contains("ROAD", Map.AcSurfaces.Keys);
        Assert.Contains("ICE", Map.AcSurfaces.Keys);
        Assert.DoesNotContain(SurfaceKeys.Wall, Map.AcSurfaces.Keys);
        Assert.Equal("ICE", Map.AcSurfaces["ICE"].ExtSurfaceType);
    }
}

public class MaterialTranslatorTests
{
    private static ExtractedMaterial Material(string shader, params (string Slot, string Path)[] textures) =>
        new(@"Stadium\Media\Material\Test.Material.Gbx", "Asphalt") { BaseShader = shader, Textures = textures.ToDictionary(t => t.Slot, t => t.Path) };

    private static MaterialTranslator Translator() => new(path => path.EndsWith(".dds", StringComparison.Ordinal) ? [1, 2, 3] : null);

    [Fact]
    public void UsesDiffuseTexture()
    {
        var translator = Translator();
        var m = translator.Translate("a", Material("TDiff_Spec_Nrm TOcc CSpecSoft", ("Normal", @"X\RoadN.dds"), ("Diffuse", @"X\RoadD.dds")))!;

        Assert.Equal(("ksPerPixel", "RoadD.dds", false), (m.Shader, m.DiffuseTexture, m.AlphaTested));
        Assert.Single(translator.Textures);
    }

    [Fact]
    public void AlphaShadersAreAlphaTested()
    {
        var m = Translator().Translate("b", Material("VDep Fence", ("FenceA", @"X\Fence.dds")))!;
        Assert.Equal(("ksPerPixelAT", true, "Fence.dds"), (m.Shader, m.AlphaTested, m.DiffuseTexture));
    }

    [Theory]
    [InlineData("TAdd Night")]
    [InlineData("TSelfI Add")]
    [InlineData("ShadowSkirt")]
    public void GlowAndFakeShadowMaterialsAreDropped(string shader) =>
        Assert.Null(Translator().Translate("c", Material(shader, ("Diffuse", @"X\Glow.dds"))));

    [Fact]
    public void MissingTexturesFallBackToFlatGrey()
    {
        var translator = Translator();
        var m = translator.Translate("d", Material("SoilFix", ("SoilFix", @"X\Soil.tga")))!;

        Assert.Equal("tm2ac_flat_grey.png", m.DiffuseTexture);
        Assert.Single(translator.Warnings);
    }
}

[Trait("Category", "Assets")]
public sealed class TmnfAssetTests : IDisposable
{
    private readonly string? _tmnf = OperatingSystem.IsWindows() ? TrackmaniaInstalls.FindTmnf() : null;
    private readonly TmnfBlockLibrary? _library;

    public TmnfAssetTests()
    {
        if (_tmnf is not null)
        {
            _library = TmnfBlockLibrary.Open(_tmnf);
        }
    }

    public void Dispose() => _library?.Dispose();

    [Fact]
    public void ExtractsTurboBlockInBlockLocalSpace()
    {
        Assert.SkipWhen(_library is null, "Needs a local TMNF install.");
        var variant = _library.GetVariant("StadiumRoadMainTurbo", isGround: true, variant: 0)!;
        var all = new Geometry.MeshData();
        foreach (var part in variant.Parts)
        {
            all.Append(part.Mesh);
        }

        var (min, max) = all.Bounds();
        Assert.Equal(0, min.X, 1);
        Assert.Equal(32, max.X, 1);
        Assert.Equal(0, min.Z, 1);
        Assert.Equal(32, max.Z, 1);
        Assert.Contains(variant.Collision.Keys, k => k.Contains("Turbo", StringComparison.Ordinal));
    }

    [Fact]
    public void IndexesBlocksByIdentNotFileName()
    {
        Assert.SkipWhen(_library is null, "Needs a local TMNF install.");
        Assert.Contains("StadiumRoadMainStartLine", _library.BlockNames); // file is StadiumRoadMainStart.TMEDClassic.Gbx
        Assert.NotNull(_library.Get("StadiumRoadMainStartLine").SpawnGround);
    }

    [Fact]
    [Trait("Category", "Network")]
    public async Task StartSpawnMatchesWrGhostOnReferenceMaps()
    {
        Assert.SkipWhen(_library is null, "Needs a local TMNF install.");
        using var client = TmxClient.CreateDefault();
        foreach (var id in new long[] { 18451, 414041, 1531338, 924307 })
        {
            var track = await client.GetTrackAsync(TmGame.Tmnf, id, TestContext.Current.CancellationToken);
            var map = TmMapReader.Read(await client.DownloadMapAsync(TmGame.Tmnf, id, TestContext.Current.CancellationToken));
            var ghost = TmGhostReader.Read(await client.DownloadReplayAsync(TmGame.Tmnf, track.WrReplayId!.Value, TestContext.Current.CancellationToken));
            var start = map.Waypoints.First(w => w.Waypoint is TmWaypoint.Start or TmWaypoint.StartFinish);
            var block = _library.Get(start.Name);
            var spawn = (start.IsGround ? block.SpawnGround : block.SpawnAir) ?? block.SpawnGround!;

            var placed = BlockPlacement.For(start.Coord, start.Direction, (1, 1)).TransformPoint(spawn.Position);

            Assert.True(Vector3.Distance(placed, ghost.Samples[0].Position) < 0.05f, $"#{id}: spawn {placed} vs ghost {ghost.Samples[0].Position}");
        }
    }
}
