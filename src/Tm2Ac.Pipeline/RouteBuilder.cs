using System.Numerics;
using SkiaSharp;
using Tm2Ac.AcTrack;
using Tm2Ac.Gbx;
using Tm2Ac.Geometry;

namespace Tm2Ac.Pipeline;

/// <summary>
/// Turns a map's waypoints (and the ghost, when there is one) into AC timing and spawn dummies (SPEC §7.2–7.3).
/// Gates sit on their block's centre plane, span the whole 32 m cell and face the direction the ghost crossed them.
/// Works in TM space; positions go through <see cref="TmnfSceneBuilder.ToAc"/>.
/// </summary>
public sealed class RouteBuilder(TmnfSceneBuilder scene, Func<TmBlock, (Vector3 Position, Vector3 Forward)> spawnOf, IssueList issues)
{
    private const float GateHalfWidth = 16f;
    /// <summary>Fallback drop from TM car-centre height to the road when there is no collision under a point.</summary>
    private const float SpawnDrop = 1.0f;

    /// <summary>Dummies sit this far above the collision surface.</summary>
    private const float DummyClearance = 0.05f;
    private const float GridSpacing = 8f;
    private const float GridLateral = 3f;
    private const float MergeCheckpointsWithinMs = 1000;
    private const float MatchRadius = 48f;

    public RaceLayout Layout { get; private set; }

    /// <summary>Ghost times (ms) at which the start/finish line was crossed, in order.</summary>
    public IReadOnlyList<int> LapLineTimes { get; private set; } = [];

    /// <summary>Checkpoint blocks in driving order (first lap), when known.</summary>
    public IReadOnlyList<TmBlock> OrderedCheckpoints { get; private set; } = [];

    public void Build(TmMap map, TmGhost? ghost, AcTrackModel track, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(options);

        var waypoints = map.Waypoints.ToList();
        var start = waypoints.FirstOrDefault(w => w.Waypoint == TmWaypoint.Start) ?? waypoints.FirstOrDefault(w => w.Waypoint == TmWaypoint.StartFinish);
        if (start is null)
        {
            issues.Add(IssueSeverity.Block, "NO_START", "", "The map has no start block");
            track.Dummies.Add(new AcDummy(AcNames.Start(0), Vector3.UnitY, Vector3.UnitZ));
            return;
        }

        var (spawnTm, forwardTm) = spawnOf(start);
        var lapBlock = waypoints.FirstOrDefault(w => w.Waypoint == TmWaypoint.StartFinish);
        var finishes = waypoints.Where(w => w.Waypoint == TmWaypoint.Finish).ToList();
        var end = ghost?.Samples[^1].Position;

        var crossings = ClassifyCrossings(waypoints, ghost);
        Layout = options.Layout ?? DetectLayout(map, lapBlock, crossings);

        var lapCheckpoints = FirstLapCheckpoints(crossings);
        // On lap races a finish block next to the line can be the nearest waypoint when the line is crossed.
        LapLineTimes = crossings
            .Where(c => c.Block.Waypoint == TmWaypoint.StartFinish || (Layout == RaceLayout.Circuit && map.Laps > 1 && c.Block.Waypoint == TmWaypoint.Finish))
            .Select(c => c.TimeMs).ToList();
        OrderedCheckpoints = lapCheckpoints;

        if (Layout == RaceLayout.Circuit && lapBlock is not null)
        {
            // AC_TIME_0 is the start/finish line, AC_TIME_n the sectors.
            AddGate(track, AcNames.TimeGate(0), lapBlock, ghost);
            for (var i = 0; i < lapCheckpoints.Count; i++)
            {
                AddGate(track, AcNames.TimeGate(i + 1), lapCheckpoints[i], ghost);
            }

            if (ghost is null)
            {
                issues.Add(IssueSeverity.Warn, "CHECKPOINT_ORDER_UNKNOWN", "", "No replay: sectors are left out because checkpoint order is unknown");
            }
        }
        else
        {
            if (Layout == RaceLayout.Circuit)
            {
                issues.Add(IssueSeverity.Warn, "LAYOUT_FALLBACK", "", "Circuit layout requested but the map has no start/finish block; using A to B");
                Layout = RaceLayout.AToB;
            }

            // A to B: the timer starts just ahead of the spawn and stops at the finish the ghost reached.
            var startGateCenter = OnGround(spawnTm + (forwardTm * 2));
            AddGate(track, AcNames.AbStart, startGateCenter, forwardTm);

            var finish = (end is { } e ? finishes.MinBy(f => Vector3.Distance(BlockCenter(f), e)) : null) ?? finishes.FirstOrDefault() ?? lapBlock;
            if (finish is null)
            {
                issues.Add(IssueSeverity.Block, "NO_FINISH", "", "The map has no finish block");
            }
            else
            {
                AddGate(track, AcNames.AbFinish, finish, ghost);
                if (finishes.Count > 1)
                {
                    issues.Add(IssueSeverity.Info, "MULTIPLE_FINISHES", "", $"The map has {finishes.Count} finish blocks; using the one at {finish.Coord}{(end is null ? "" : " (where the replay ended)")}");
                }
            }

            if (lapCheckpoints.Count > 0)
            {
                issues.Add(IssueSeverity.Info, "AB_NO_SECTORS", "", "Checkpoints aren't converted to sectors on A-to-B tracks");
            }
        }

        AddGridAndPits(track, spawnTm, forwardTm, options.Pitboxes);
        AddHotlapStart(track, spawnTm, forwardTm, ghost);
    }

    /// <summary>
    /// Each ghost checkpoint time matched to the nearest waypoint block of any kind (checkpoint, finish or start/finish).
    /// Side-by-side checkpoints crossed within a second of each other (wide roads) count once.
    /// </summary>
    private static List<(int TimeMs, TmBlock Block)> ClassifyCrossings(List<TmBlock> waypoints, TmGhost? ghost)
    {
        var crossings = new List<(int, TmBlock)>();
        var candidates = waypoints.Where(w => w.Waypoint is TmWaypoint.Checkpoint or TmWaypoint.Finish or TmWaypoint.StartFinish).ToList();
        if (ghost is null || candidates.Count == 0)
        {
            return crossings;
        }

        var lastCheckpointTime = -10_000;
        foreach (var time in ghost.CheckpointTimesMs)
        {
            var position = ghost.At(time).Position;
            var nearest = candidates.MinBy(c => Vector3.Distance(BlockCenter(c), position))!;
            if (Vector3.Distance(BlockCenter(nearest), position) > MatchRadius)
            {
                continue;
            }

            if (nearest.Waypoint == TmWaypoint.Checkpoint)
            {
                if (time - lastCheckpointTime < MergeCheckpointsWithinMs)
                {
                    continue;
                }

                lastCheckpointTime = time;
            }

            crossings.Add((time, nearest));
        }

        return crossings;
    }

    /// <summary>
    /// Circuit when the ghost crosses the start/finish line before its last checkpoint time (it completed a lap and kept
    /// going) or ends there; otherwise A to B. Without a ghost, the map's own lap flag decides.
    /// </summary>
    private static RaceLayout DetectLayout(TmMap map, TmBlock? lapBlock, List<(int TimeMs, TmBlock Block)> crossings)
    {
        if (lapBlock is null)
        {
            return RaceLayout.AToB;
        }

        if (crossings.Count == 0)
        {
            return map.IsMultilap ? RaceLayout.Circuit : RaceLayout.AToB;
        }

        var lapCrossings = crossings.Count(c => c.Block.Waypoint == TmWaypoint.StartFinish);
        var midRaceLap = crossings.Take(crossings.Count - 1).Any(c => c.Block.Waypoint == TmWaypoint.StartFinish || (map.Laps > 1 && c.Block.Waypoint == TmWaypoint.Finish));
        return lapCrossings > 0 || midRaceLap ? RaceLayout.Circuit : RaceLayout.AToB;
    }

    /// <summary>Checkpoint blocks up to the first lap or finish crossing, in driving order.</summary>
    private static List<TmBlock> FirstLapCheckpoints(List<(int TimeMs, TmBlock Block)> crossings) =>
        crossings.TakeWhile(c => c.Block.Waypoint == TmWaypoint.Checkpoint).Select(c => c.Block).Distinct().ToList();

    private void AddGate(AcTrackModel track, (string Left, string Right) names, TmBlock block, TmGhost? ghost)
    {
        var center = BlockCenter(block);
        var forward = BlockForward(block);
        if (ghost is not null)
        {
            // Height and direction from where the ghost actually crossed the block.
            var nearest = ghost.Samples.MinBy(s => Vector3.DistanceSquared(new Vector3(s.Position.X, 0, s.Position.Z), new Vector3(center.X, 0, center.Z)))!;
            if (Vector3.Distance(nearest.Position, center) < MatchRadius)
            {
                center = OnGround(center with { Y = nearest.Position.Y });
                if (Vector3.Dot(nearest.Velocity, forward) < 0)
                {
                    forward = -forward;
                }
            }
        }

        AddGate(track, names, center, forward);
    }

    private void AddGate(AcTrackModel track, (string Left, string Right) names, Vector3 centerTm, Vector3 forwardTm)
    {
        var flat = Vector3.Normalize(new Vector3(forwardTm.X, 0, forwardTm.Z));
        track.Dummies.AddRange(AcDummies.Gate(names, scene.ToAc(centerTm), flat, GateHalfWidth * scene.Scale));
    }

    /// <summary>
    /// N grid slots (and pit boxes at the same spots) staggered behind the spawn. If any slot has no drivable surface under it,
    /// a flat platform is generated behind the start block (SPEC §7.3 strategy 2).
    /// </summary>
    private void AddGridAndPits(AcTrackModel track, Vector3 spawnTm, Vector3 forwardTm, int count)
    {
        count = Math.Clamp(count, 1, 40);
        var forward = Vector3.Normalize(new Vector3(forwardTm.X, 0, forwardTm.Z));
        var left = AcAxes.Left(forward);
        // Slots at car-centre height; each is then snapped to the surface under it (start pads are raised, see S5).
        var slots = new List<Vector3>();
        for (var n = 0; n < count; n++)
        {
            var lateral = n == 0 ? 0 : (n % 2 == 1 ? -GridLateral : GridLateral);
            slots.Add(spawnTm - (forward * GridSpacing * n) + (left * lateral));
        }

        float? platformY = null;
        if (slots.Skip(1).Any(s => GroundHeight(s) is null))
        {
            platformY = (GroundHeight(spawnTm) ?? (spawnTm.Y - SpawnDrop)) - 0.3f;
            AddPlatform(track, spawnTm with { Y = platformY.Value }, forward, left, count);
            issues.Add(IssueSeverity.Info, "PIT_PLATFORM_GENERATED", "", "Not enough road behind the start for the grid; a flat platform was added behind the start block");
        }

        for (var n = 0; n < slots.Count; n++)
        {
            var grounded = GroundHeight(slots[n]) is { } y ? slots[n] with { Y = y + DummyClearance }
                : platformY is { } py ? slots[n] with { Y = py + DummyClearance }
                : slots[n] - new Vector3(0, SpawnDrop, 0);
            var ac = scene.ToAc(grounded);
            track.Dummies.Add(new AcDummy(AcNames.Start(n), ac, forward));
            track.Dummies.Add(new AcDummy(AcNames.Pit(n), ac, forward));
        }
    }

    private void AddPlatform(AcTrackModel track, Vector3 nearCenterTm, Vector3 forward, Vector3 left, int count)
    {
        const float halfWidth = 8f;
        var near = nearCenterTm - (forward * 4);
        var far = nearCenterTm - (forward * ((GridSpacing * count) + 8));
        Vector3[] cornersTm = [near + (left * halfWidth), near - (left * halfWidth), far - (left * halfWidth), far + (left * halfWidth)];

        var visual = new MeshData();
        var collision = new MeshData();
        var length = Vector3.Distance(near, far);
        Vector2[] uvs = [new(0, 0), new(1, 0), new(1, length / 16), new(0, length / 16)];
        for (var i = 0; i < 4; i++)
        {
            var ac = scene.ToAc(cornersTm[i]);
            visual.AddVertex(ac, AcAxes.Up, uvs[i]);
            collision.Positions.Add(ac);
        }

        // near-left, near-right, far-right, far-left: facing forward, right is -left; wind so the face points up.
        visual.AddQuad(0, 3, 2, 1);
        collision.AddQuad(0, 3, 2, 1);

        const string texture = "tm2ac_platform.png";
        if (!track.Textures.Any(t => t.Name == texture))
        {
            using var bitmap = new SKBitmap(64, 64);
            bitmap.Erase(new SKColor(0x55, 0x58, 0x5E));
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            track.Textures.Add(new AcTexture(texture, data.ToArray()));
            track.Materials.Add(new AcMaterial("tm2ac_platform", texture));
        }

        track.VisualMeshes.Add(new AcVisualMesh("tm2ac_grid_platform", "tm2ac_platform", visual) { CastShadows = false });
        track.CollisionMeshes.Add(new AcCollisionMesh(SurfaceKeys.Road, collision));
        if (!track.Surfaces.Any(s => s.Key == SurfaceKeys.Road))
        {
            track.Surfaces.Add(SurfaceMap.Tmnf.AcSurfaces[SurfaceKeys.Road]);
        }
    }

    /// <summary>
    /// Hotlap start: on a circuit, the ghost's position ~4 s before it first completes the lap (on the run-up to the line);
    /// otherwise the spawn.
    /// </summary>
    private void AddHotlapStart(AcTrackModel track, Vector3 spawnTm, Vector3 forwardTm, TmGhost? ghost)
    {
        var position = OnGround(spawnTm);
        var forward = forwardTm;
        if (Layout == RaceLayout.Circuit && ghost is not null)
        {
            var lapTime = ghost.CheckpointTimesMs.Count > OrderedCheckpoints.Count ? ghost.CheckpointTimesMs[OrderedCheckpoints.Count] : ghost.RaceTimeMs;
            var sample = ghost.At(Math.Max(0, lapTime - 4000));
            if (sample.WheelsOnGround > 0 && sample.Velocity.LengthSquared() > 1)
            {
                position = OnGround(sample.Position);
                forward = sample.Velocity;
            }
        }

        track.Dummies.Add(new AcDummy(AcNames.HotlapStart, scene.ToAc(position), Vector3.Normalize(new Vector3(forward.X, 0, forward.Z))));
    }

    /// <summary>Drivable surface height under a car-centre position (TM space), searching 4 m down; null if none.</summary>
    private float? GroundHeight(Vector3 carCentreTm) => scene.SurfaceHeightBelow(carCentreTm + new Vector3(0, 0.5f, 0), 4f);

    /// <summary>A car-centre position moved down onto the surface under it (falls back to a fixed drop).</summary>
    private Vector3 OnGround(Vector3 carCentreTm) =>
        GroundHeight(carCentreTm) is { } y ? carCentreTm with { Y = y + DummyClearance } : carCentreTm - new Vector3(0, SpawnDrop, 0);

    /// <summary>Centre of a 1×1 block at road height (TM space).</summary>
    private static Vector3 BlockCenter(TmBlock block) =>
        new((block.Coord.X * BlockPlacement.BlockWidth) + 16, (block.Coord.Y * BlockPlacement.BlockHeight) + 2, (block.Coord.Z * BlockPlacement.BlockWidth) + 16);

    private static Vector3 BlockForward(TmBlock block) => BlockPlacement.For(block.Coord, block.Direction, (1, 1)).TransformDirection(Vector3.UnitZ);
}
