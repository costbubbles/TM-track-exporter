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

public class IssueListTests
{
    [Fact]
    public void AggregatesRepeatsAndSortsBySeverity()
    {
        var issues = new IssueList();
        issues.Add(IssueSeverity.Warn, "MISSING_ASSET", "A", "No geometry for A");
        issues.Add(IssueSeverity.Warn, "MISSING_ASSET", "A", "No geometry for A");
        issues.Add(IssueSeverity.Info, "ASSET_FALLBACK", "B", "B uses C");
        issues.Add(IssueSeverity.Block, "NO_START", "", "No start");

        var list = issues.ToList();

        Assert.Equal(["NO_START", "MISSING_ASSET", "ASSET_FALLBACK"], list.Select(i => i.Code));
        Assert.Equal("No geometry for A (x2)", list[1].Message);
    }
}

[Trait("Category", "Assets")]
[Trait("Category", "Network")]
public sealed class TmnfConverterTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "tm2ac-convert-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_out))
        {
            Directory.Delete(_out, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertsReferenceMapR1()
    {
        var tmnf = OperatingSystem.IsWindows() ? TrackmaniaInstalls.FindTmnf() : null;
        Assert.SkipWhen(tmnf is null, "Needs a local TMNF install.");
        using var library = TmnfBlockLibrary.Open(tmnf!);
        using var client = TmxClient.CreateDefault();
        var source = await ConversionSource.FromTmxAsync(client, TmGame.Tmnf, 18451, cancellationToken: TestContext.Current.CancellationToken);

        var result = new TmnfConverter(library).Convert(source, new ConversionOptions(), _out);

        Assert.Equal("tmnf_18451_always_be_mine", result.TrackId);
        Assert.Equal(227, result.Blocks);
        Assert.True(result.VisualTriangles > 100_000);
        Assert.DoesNotContain(result.Issues, i => i.Code is "MISSING_ASSET" or "NO_START");

        var collision = Kn5.Kn5Reader.Read(Path.Combine(result.Directory, "collision.kn5"));
        var names = collision.Root.Children.Select(c => c.Name).ToList();
        Assert.Contains(names, n => n.StartsWith("1ROAD", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("1GRASS", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("1WALL", StringComparison.Ordinal));

        // The AC spawn is the TM start spawn moved to AC space: the map is 1024 m wide, so x/z are centred.
        var visual = Kn5.Kn5Reader.Read(Path.Combine(result.Directory, result.TrackId + ".kn5"));
        var start = visual.Root.Children.OfType<Kn5.Kn5DummyNode>().Single(d => d.Name == "AC_START_0");
        Assert.Equal(820.8f - 512, start.Transform.Translation.X, 1);
        Assert.Equal(304f - 512, start.Transform.Translation.Z, 1);
        Assert.Equal(-1f, start.Transform.M31, 3); // facing -X like the ghost
    }
}

[Trait("Category", "Assets")]
[Trait("Category", "Network")]
public sealed class RouteTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "tm2ac-route-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_out))
        {
            Directory.Delete(_out, recursive: true);
        }
    }

    private async Task<(ConversionResult Result, List<Kn5.Kn5DummyNode> Dummies, System.Text.Json.Nodes.JsonNode Ui)> Convert(long id)
    {
        var tmnf = OperatingSystem.IsWindows() ? TrackmaniaInstalls.FindTmnf() : null;
        Assert.SkipWhen(tmnf is null, "Needs a local TMNF install.");
        using var library = TmnfBlockLibrary.Open(tmnf!);
        using var client = TmxClient.CreateDefault();
        var source = await ConversionSource.FromTmxAsync(client, TmGame.Tmnf, id, cancellationToken: TestContext.Current.CancellationToken);
        var result = new TmnfConverter(library).Convert(source, new ConversionOptions { Pitboxes = 6 }, _out);
        var visual = Kn5.Kn5Reader.Read(Path.Combine(result.Directory, result.TrackId + ".kn5"));
        var ui = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(result.Directory, "ui", "ui_track.json")))!;
        return (result, visual.Root.Children.OfType<Kn5.Kn5DummyNode>().ToList(), ui);
    }

    [Fact]
    public async Task PointToPointMapGetsAbGatesAndGrid()
    {
        var (_, dummies, ui) = await Convert(18451);
        var names = dummies.Select(d => d.Name).ToHashSet();

        Assert.Contains("AC_AB_START_L", names);
        Assert.Contains("AC_AB_FINISH_R", names);
        Assert.DoesNotContain("AC_TIME_0_L", names);
        Assert.Equal(6, names.Count(n => n.StartsWith("AC_START_", StringComparison.Ordinal)));
        Assert.Equal("a2b", (string?)ui["run"]);
    }

    [Fact]
    public async Task MultilapMapGetsLineAndSectorsInDrivingOrder()
    {
        var (_, dummies, ui) = await Convert(1531338);
        var names = dummies.Select(d => d.Name).ToHashSet();

        Assert.Contains("AC_TIME_0_L", names);
        Assert.Contains("AC_TIME_4_R", names); // 4 checkpoint blocks -> 4 sectors after the line
        Assert.DoesNotContain("AC_AB_START_L", names);
        Assert.Equal("clockwise", (string?)ui["run"]);

        // Grid slots sit behind the start/finish line, facing the same way as the gate.
        var gateL = dummies.Single(d => d.Name == "AC_TIME_0_L").Transform;
        var gateR = dummies.Single(d => d.Name == "AC_TIME_0_R").Transform;
        var gateCenter = (gateL.Translation + gateR.Translation) / 2;
        var forward = new Vector3(gateL.M31, gateL.M32, gateL.M33);
        var grid = dummies.Where(d => d.Name.StartsWith("AC_START_", StringComparison.Ordinal)).ToList();
        Assert.All(grid, d => Assert.True(Vector3.Dot(d.Transform.Translation - gateCenter, forward) < 0, $"{d.Name} is not behind the line"));
    }

    [Fact]
    public async Task LapRaceEndingNextToADecorativeFinishIsStillACircuit()
    {
        var (_, dummies, ui) = await Convert(11023114); // TWC Copenhagen: 2 laps, finish block 16 m from the lap line
        Assert.Contains(dummies, d => d.Name == "AC_TIME_0_L");
        Assert.NotEqual("a2b", (string?)ui["run"]);
    }
}

public class AiLineMathTests
{
    [Fact]
    public void ResamplesAtEvenSpacing()
    {
        var points = AiLineBuilder.Resample([Vector3.Zero, new Vector3(0, 0, 10), new Vector3(10, 0, 10)], 1.5f);

        Assert.Equal(new Vector3(0, 0, 0), points[0]);
        for (var i = 1; i < points.Count; i++)
        {
            Assert.InRange(Vector3.Distance(points[i - 1], points[i]), 1.0f, 1.5001f); // spaced by arc length; the chord across the corner is shorter
        }

        Assert.Equal(14, points.Count); // 20 m / 1.5 m + start
    }

    [Fact]
    public void SpeedDropsIntoCornersAndBrakesBeforeThem()
    {
        // 450 m straight, then a tight 20 m radius corner.
        var straight = Enumerable.Range(0, 300).Select(i => new Vector3(0, 0, i * 1.5f));
        var corner = Enumerable.Range(1, 30).Select(i =>
        {
            var a = i / 30f * MathF.PI / 2;
            return new Vector3(-20 + (20 * MathF.Cos(a)), 0, 448.5f + (20 * MathF.Sin(a)));
        });
        var ai = AiLineBuilder.Annotate([.. straight, .. corner], closed: false, scale: 1);

        var cornerSpeed = ai[315].Speed;
        Assert.InRange(cornerSpeed, 14, 20);                                   // sqrt(1.4 * 9.81 * 20) ≈ 16.6 m/s
        Assert.True(ai[60].Speed > cornerSpeed * 3);                            // much faster on the straight
        Assert.Equal(1f, ai[20].Gas);                                          // flat out early on the straight
        Assert.Contains(ai.Skip(200).Take(100), p => p.Brake == 1f);           // braking in the run-up to the corner
    }
}

public class CompatibilityRatingTests
{
    [Fact]
    public void RatingFollowsWorstSeverity()
    {
        Assert.Equal(CompatibilityRating.Green, CompatibilityAnalyzer.Rate([new ConversionIssue(IssueSeverity.Info, "X", "")]));
        Assert.Equal(CompatibilityRating.Yellow, CompatibilityAnalyzer.Rate([new ConversionIssue(IssueSeverity.Info, "X", ""), new ConversionIssue(IssueSeverity.Warn, "Y", "")]));
        Assert.Equal(CompatibilityRating.Red, CompatibilityAnalyzer.Rate([new ConversionIssue(IssueSeverity.Warn, "Y", ""), new ConversionIssue(IssueSeverity.Block, "Z", "")]));
    }

    private static TmGhost Ghost(params TmGhostSample[] samples) => new(samples.Length * 100, [], samples, 100);

    private static TmGhostSample Sample(int i, Vector3 up, int wheels, string surface = "Asphalt") => new(i * 100, Vector3.Zero, Vector3.UnitZ, up, wheels, surface);

    [Fact]
    public void MeasuresUpsideDownWallsAirAndBoosters()
    {
        var samples = new List<TmGhostSample>();
        samples.AddRange(Enumerable.Range(0, 10).Select(i => Sample(i, Vector3.UnitY, 4)));
        samples.AddRange(Enumerable.Range(10, 5).Select(i => Sample(i, -Vector3.UnitY, 4)));                 // upside down 0.5 s
        samples.AddRange(Enumerable.Range(15, 3).Select(i => Sample(i, Vector3.UnitX, 4)));                  // on a wall 0.3 s
        samples.AddRange(Enumerable.Range(18, 25).Select(i => Sample(i, Vector3.UnitY, 0)));                 // 2.5 s flight
        samples.AddRange(Enumerable.Range(43, 4).Select(i => Sample(i, Vector3.UnitY, 4, "Turbo_Deprecated"))); // 0.4 s boost

        var stats = CompatibilityAnalyzer.Measure(Ghost([.. samples]));

        Assert.Equal((500, 300, 2500, 400), (stats.UpsideDownMs, stats.WallDrivingMs, stats.LongestAirMs, stats.BoosterContactMs));
    }
}

[Trait("Category", "Network")]
public sealed class CompatibilityLiveTests
{
    private static async Task<CompatibilityRating> RateMap(long id)
    {
        using var client = TmxClient.CreateDefault();
        var track = await client.GetTrackAsync(TmGame.Tmnf, id, TestContext.Current.CancellationToken);
        var map = TmMapReader.Read(await client.DownloadMapAsync(TmGame.Tmnf, id, TestContext.Current.CancellationToken));
        var ghost = TmGhostReader.Read(await client.DownloadReplayAsync(TmGame.Tmnf, track.WrReplayId!.Value, TestContext.Current.CancellationToken));
        var issues = new IssueList();
        CompatibilityAnalyzer.Analyze(map, ghost, issues);
        return CompatibilityAnalyzer.Rate(issues.ToList());
    }

    [Theory]
    [InlineData(1531338, CompatibilityRating.Green)]  // R3 Rockridge: flat circuit
    [InlineData(509742, CompatibilityRating.Yellow)]  // R4 Dirty Dreams: 2.5 s jumps, boosters
    [InlineData(93481, CompatibilityRating.Red)]      // R5 Smooth Life: loop (upside down 1.4 s)
    [InlineData(924307, CompatibilityRating.Red)]     // R7 PressForward: loops, 9 s flight
    public async Task RatesReferenceMaps(long id, CompatibilityRating expected) => Assert.Equal(expected, await RateMap(id));
}
