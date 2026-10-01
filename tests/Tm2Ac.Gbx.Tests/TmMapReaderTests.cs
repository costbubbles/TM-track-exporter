using Tm2Ac.Core;
using Tm2Ac.Tmx;

namespace Tm2Ac.Gbx.Tests;

public class BlockFlagTableTests
{
    [Theory]
    [InlineData("StadiumRoadMainStartLine", TmWaypoint.Start)]
    [InlineData("StadiumRoadMainFinishLine", TmWaypoint.Finish)]
    [InlineData("StadiumRoadMainStartFinishLine", TmWaypoint.StartFinish)]
    [InlineData("StadiumCheckpointRingV", TmWaypoint.Checkpoint)]
    [InlineData("StadiumRoadDirtHighCheckpoint", TmWaypoint.Checkpoint)]
    [InlineData("StadiumRoadMainBiSlopeStart", TmWaypoint.None)] // "Start" in the name but not a waypoint
    [InlineData("StadiumCircuitLoopStart", TmWaypoint.None)]
    [InlineData("SomethingUnknown", TmWaypoint.None)]
    public void ClassifiesTmnfWaypoints(string block, TmWaypoint expected) => Assert.Equal(expected, BlockFlagTable.Tmnf.Waypoint(block));

    [Fact]
    public void EmbeddedTableHasAllEighteenWaypointBlocks() => Assert.Equal(18, BlockFlagTable.Tmnf.Count - 4); // + 4 fallback-only entries

    [Fact]
    public void HasFallbacksForUnparseableBlocks() => Assert.Equal("StadiumRoadMainTurboRouletteLeft", BlockFlagTable.Tmnf.Fallback("StadiumRoadMainTurboLeft"));
}

public class MoodTests
{
    [Theory]
    [InlineData("Day", "Day")]
    [InlineData("Night", "Night")]
    [InlineData("48x48Screen155Day", "Day")]
    [InlineData("48x48Screen155Sunset", "Sunset")]
    [InlineData(null, "Day")]
    [InlineData("Weird", "Weird")]
    public void ExtractsMoodFromDecoration(string? decoration, string expected) => Assert.Equal(expected, TmMapReader.MoodFromDecoration(decoration));
}

[Trait("Category", "Network")]
public sealed class TmMapReaderLiveTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "tm2ac-gbx-live-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    private async Task<TmMap> Fetch(long id)
    {
        using var client = TmxClient.CreateDefault(_cacheDir);
        return TmMapReader.Read(await client.DownloadMapAsync(TmGame.Tmnf, id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ParsesReferenceMapR1()
    {
        var map = await Fetch(18451);

        Assert.Equal("Stadium", map.Collection);
        Assert.Equal(new GridCoord(32, 32, 32), map.Size);
        Assert.Equal(227, map.Blocks.Count);
        Assert.False(map.IsMultilap);
        Assert.Single(map.Waypoints, w => w.Waypoint == TmWaypoint.Start);
        Assert.Equal(4, map.Waypoints.Count(w => w.Waypoint == TmWaypoint.Checkpoint));
    }

    [Fact]
    public async Task ParsesMultilapReferenceMapR3()
    {
        var map = await Fetch(1531338);

        Assert.True(map.IsMultilap);
        Assert.Single(map.Waypoints, w => w.Waypoint == TmWaypoint.StartFinish);
    }

    [Fact]
    public async Task StartBlockMatchesKnownPositionOnR7()
    {
        var map = await Fetch(924307);
        var start = Assert.Single(map.Waypoints, w => w.Waypoint == TmWaypoint.Start);

        Assert.Equal(("StadiumRoadMainStartLine", new GridCoord(16, 29, 29), TmDirection.South), (start.Name, start.Coord, start.Direction));
    }
}
