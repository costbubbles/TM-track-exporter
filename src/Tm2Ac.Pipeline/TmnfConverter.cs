using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using SkiaSharp;
using Tm2Ac.AcTrack;
using Tm2Ac.Assets;
using Tm2Ac.Core;
using Tm2Ac.Gbx;
using Tm2Ac.Geometry;
using Tm2Ac.Tmx;

namespace Tm2Ac.Pipeline;

/// <summary>Everything a conversion needs: the map file, optionally a ghost and TMX metadata.</summary>
public sealed record ConversionSource(TmGame Game, long TmxId, string MapPath)
{
    public string? GhostPath { get; init; }
    public TmxTrack? Track { get; init; }
    public byte[]? Screenshot { get; init; }

    /// <summary>Downloads map, WR replay and first screenshot from TMX (all cached).</summary>
    public static async Task<ConversionSource> FromTmxAsync(TmxClient client, TmGame game, long tmxId, long? replayId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var track = await client.GetTrackAsync(game, tmxId, cancellationToken).ConfigureAwait(false);
        var map = await client.DownloadMapAsync(game, tmxId, cancellationToken).ConfigureAwait(false);

        string? ghost = null;
        if ((replayId ?? track.WrReplayId) is { } id)
        {
            ghost = await client.DownloadReplayAsync(game, id, cancellationToken).ConfigureAwait(false);
        }

        byte[]? screenshot = null;
        try
        {
            screenshot = await File.ReadAllBytesAsync(await client.DownloadImageAsync(game, tmxId, track.ScreenshotCount > 0 ? 1 : 0, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TmxNotFoundException or HttpRequestException)
        {
            // No screenshot: a generated preview is used.
        }

        return new ConversionSource(game, tmxId, map) { GhostPath = ghost, Track = track, Screenshot = screenshot };
    }
}

public sealed record ConversionResult(string TrackId, string Directory, IReadOnlyList<ConversionIssue> Issues)
{
    public CompatibilityRating Rating { get; init; }

    /// <summary>True when the map was rated Red and not converted (use <see cref="ConversionOptions.Force"/>).</summary>
    public bool Refused { get; init; }

    public GhostStats? GhostStats { get; init; }

    public int Blocks { get; init; }
    public int VisualTriangles { get; init; }
    public int CollisionTriangles { get; init; }
    public TimeSpan Elapsed { get; init; }
}

/// <summary>TMNF map → installed AC track folder (SPEC §7).</summary>
public sealed class TmnfConverter(TmnfBlockLibrary library)
{
    /// <summary>TM spawn points sit at car-centre height; AC dummies sit near the road surface.</summary>
    private const float SpawnDrop = 1.0f;

    /// <param name="progress">Receives short step descriptions (for logs and UIs).</param>
    /// <param name="cancellationToken">Checked between steps; nothing is written if cancelled before the final write.</param>
    public ConversionResult Convert(ConversionSource source, ConversionOptions options, string tracksDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        var stopwatch = Stopwatch.StartNew();
        var issues = new IssueList();

        progress?.Report("Reading map and replay");
        var map = TmMapReader.Read(source.MapPath);
        var ghost = source.GhostPath is null ? null : TryReadGhost(source.GhostPath, issues);
        if (ghost is null)
        {
            issues.Add(IssueSeverity.Warn, "NO_REPLAY", "", "No replay available: layout and sectors are guessed from the map, and the minimap uses waypoints");
        }

        var name = source.Track?.Name is { Length: > 0 } tmxName ? TmText.StripFormatting(tmxName) : map.Name;
        var trackId = TrackIds.Create(source.Game, source.TmxId, name);
        var directory = Path.Combine(tracksDirectory, trackId);

        // Cheap checks first: a Red map is refused before any geometry is built.
        progress?.Report("Checking compatibility");
        var stats = CompatibilityAnalyzer.Analyze(map, ghost, issues);
        if (!options.Force && CompatibilityAnalyzer.Rate(issues.ToList()) == CompatibilityRating.Red)
        {
            return new ConversionResult(trackId, directory, issues.ToList()) { Rating = CompatibilityRating.Red, Refused = true, GhostStats = stats, Elapsed = stopwatch.Elapsed };
        }

        var builder = new TmnfSceneBuilder(library, issues);

        var track = new AcTrackModel
        {
            Id = trackId,
            Ui = BuildUi(source, map, name),
            MapPath = new Centerline([Vector3.Zero, Vector3.UnitZ], closed: false), // replaced once the scene transform is known
            ExtendedPhysics = true,
            PreviewPng = source.Screenshot is { } shot ? Preview(shot) : null,
        };

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report($"Building geometry from {map.Blocks.Count} blocks");
        builder.Build(map, track, options);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Placing timing gates, grid and pits");
        var route = new RouteBuilder(builder, SpawnOf, issues);
        route.Build(map, ghost, track, options);
        var circuit = route.Layout == RaceLayout.Circuit;

        track.MapPath = new Centerline(MapPath(map, ghost, builder, circuit), closed: circuit);
        track.Ui = track.Ui with
        {
            Run = circuit ? RunDirection(track.MapPath) : "a2b",
            Tags = [.. track.Ui.Tags.Where(t => t is not ("circuit" or "a2b")), circuit ? "circuit" : "a2b"],
        };

        var issueList = issues.ToList();
        var rating = CompatibilityAnalyzer.Rate(issueList);
        var report = Report(source, map, options, issueList, builder);
        report["compatibility"] = rating.ToString();
        if (stats is not null)
        {
            report["ghostStats"] = new JsonObject
            {
                ["raceTimeMs"] = stats.RaceTimeMs,
                ["upsideDownMs"] = stats.UpsideDownMs,
                ["wallDrivingMs"] = stats.WallDrivingMs,
                ["longestAirMs"] = stats.LongestAirMs,
                ["airbornePercent"] = stats.AirbornePercent,
                ["boosterContactMs"] = stats.BoosterContactMs,
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report($"Writing {trackId} ({builder.VisualTriangles:N0} visual triangles)");
        AcTrackWriter.Write(track, directory, report);
        return new ConversionResult(trackId, directory, issueList)
        {
            Rating = rating,
            GhostStats = stats,
            Blocks = builder.PlacedBlocks,
            VisualTriangles = builder.VisualTriangles,
            CollisionTriangles = builder.CollisionTriangles,
            Elapsed = stopwatch.Elapsed,
        };
    }

    private static TmGhost? TryReadGhost(string path, IssueList issues)
    {
        try
        {
            return TmGhostReader.Read(path);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IOException)
        {
            issues.Add(IssueSeverity.Warn, "REPLAY_UNREADABLE", path, $"Replay could not be read: {e.Message}");
            return null;
        }
    }

    /// <summary>"clockwise" or "anticlockwise" as seen on map.png (x right, z down).</summary>
    private static string RunDirection(Centerline path)
    {
        double area = 0;
        for (var i = 0; i < path.Count; i++)
        {
            var a = path.Points[i];
            var b = path.Points[(i + 1) % path.Count];
            area += (a.X * b.Z) - (b.X * a.Z);
        }

        return area > 0 ? "clockwise" : "anticlockwise";
    }

    /// <summary>World-space spawn of a start block (TM space).</summary>
    public (Vector3 Position, Vector3 Forward) SpawnOf(TmBlock start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var block = library.Get(start.Name);
        var spawn = (start.IsGround ? block.SpawnGround ?? block.SpawnAir : block.SpawnAir ?? block.SpawnGround)
            ?? new BlockSpawn(new Vector3(16, 2, 16), Vector3.UnitZ);
        var placement = BlockPlacement.For(start.Coord, start.Direction, (1, 1));
        return (placement.TransformPoint(spawn.Position), Vector3.Normalize(placement.TransformDirection(spawn.Forward)));
    }

    /// <summary>Driving line for map.png: the ghost path when available, else start → checkpoints → finish.</summary>
    private static List<Vector3> MapPath(TmMap map, TmGhost? ghost, TmnfSceneBuilder builder, bool firstLapOnly)
    {
        var points = new List<Vector3>();
        if (ghost is not null)
        {
            // For circuits, draw one lap: samples up to the first lap's end (race time / laps as an approximation).
            var lapEnd = firstLapOnly && map.Laps > 1 ? ghost.RaceTimeMs / map.Laps : int.MaxValue;
            foreach (var sample in ghost.Samples.Where(s => s.TimeMs <= lapEnd))
            {
                var p = builder.ToAc(sample.Position);
                if (points.Count == 0 || Vector3.Distance(points[^1], p) >= 2)
                {
                    points.Add(p);
                }
            }
        }

        if (points.Count < 2)
        {
            points = map.Waypoints
                .OrderBy(w => w.Waypoint switch { TmWaypoint.Start or TmWaypoint.StartFinish => 0, TmWaypoint.Checkpoint => 1, _ => 2 })
                .Select(w => builder.ToAc(new Vector3((w.Coord.X * 32) + 16, w.Coord.Y * 8, (w.Coord.Z * 32) + 16)))
                .ToList();
        }

        if (points.Count < 2)
        {
            points = [Vector3.Zero, Vector3.UnitZ];
        }

        return points;
    }

    private static UiTrackInfo BuildUi(ConversionSource source, TmMap map, string name)
    {
        var track = source.Track;
        var author = track is { Authors.Count: > 0 } ? string.Join(", ", track.Authors) : map.Author;
        var url = track?.PageUri.ToString() ?? "";
        var description = $"Converted from {source.Game.Id().ToUpperInvariant()}-X #{source.TmxId} by Tm2Ac.";
        if (track is { Description.Length: > 0 })
        {
            description = $"{TmText.StripFormatting(track.Description)}\n\n{description}";
        }

        return new UiTrackInfo(name)
        {
            Description = description,
            Tags = ["trackmania", "tm2ac", source.Game.Id(), map.IsMultilap ? "circuit" : "a2b"],
            Country = "Trackmania",
            City = map.Collection,
            Author = author,
            Version = "tm2ac",
            Url = url,
            Year = track?.UploadedAt.Year,
            Run = map.IsMultilap ? "clockwise" : "a2b",
        };
    }

    /// <summary>TMX screenshot scaled and centre-cropped to CM's 355×200 preview; null if it can't be decoded.</summary>
    public static byte[]? Preview(byte[] image)
    {
        using var source = SKBitmap.Decode(image);
        if (source is null)
        {
            return null;
        }

        const int width = MapImages.PreviewWidth;
        const int height = MapImages.PreviewHeight;
        var scale = Math.Max((float)width / source.Width, (float)height / source.Height);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        var dest = SKRect.Create((width - (source.Width * scale)) / 2, (height - (source.Height * scale)) / 2, source.Width * scale, source.Height * scale);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(SKImage.FromBitmap(source), dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static JsonObject Report(ConversionSource source, TmMap map, ConversionOptions options, IReadOnlyList<ConversionIssue> issues, TmnfSceneBuilder builder) => new()
    {
        ["source"] = new JsonObject
        {
            ["game"] = source.Game.Id(),
            ["tmxId"] = source.TmxId,
            ["mapUid"] = map.Uid,
            ["mapName"] = map.Name,
            ["ghost"] = source.GhostPath is null ? null : Path.GetFileName(source.GhostPath),
        },
        ["options"] = new JsonObject
        {
            ["scale"] = options.Scale,
            ["pitboxes"] = options.Pitboxes,
            ["defaultGrass"] = options.DefaultGrass,
            ["edgeWalls"] = options.EdgeWalls,
        },
        ["stats"] = new JsonObject
        {
            ["blocks"] = builder.PlacedBlocks,
            ["visualTriangles"] = builder.VisualTriangles,
            ["collisionTriangles"] = builder.CollisionTriangles,
        },
        ["issues"] = new JsonArray(issues.Select(i => (JsonNode)new JsonObject
        {
            ["severity"] = i.Severity.ToString(),
            ["code"] = i.Code,
            ["message"] = i.Message,
        }).ToArray()),
        ["converterVersion"] = typeof(TmnfConverter).Assembly.GetName().Version?.ToString(3) ?? "0",
        ["convertedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    };
}
