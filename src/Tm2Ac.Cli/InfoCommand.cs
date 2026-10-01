using System.CommandLine;
using System.Globalization;
using Tm2Ac.Core;
using Tm2Ac.Gbx;
using Tm2Ac.Tmx;

namespace Tm2Ac.Cli;

internal static class InfoCommand
{
    public static Command Create()
    {
        var game = CliCommon.GameArgument();
        var target = new Argument<string>("track") { Description = "TMX track id, or a path to a .Challenge.Gbx / .Map.Gbx file." };
        var command = new Command("info", "Show TMX metadata and a summary of the map's blocks and waypoints.") { game, target };
        command.SetAction(async (result, cancellationToken) =>
        {
            if (!CliCommon.TryGetGame(result.GetValue(game)!, out var tmGame))
            {
                return 1;
            }

            var value = result.GetValue(target)!;
            string mapPath;
            TmxTrack? track = null;
            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var trackId))
            {
                using var client = TmxClient.CreateDefault();
                try
                {
                    track = await client.GetTrackAsync(tmGame, trackId, cancellationToken);
                }
                catch (TmxNotFoundException e)
                {
                    Console.Error.WriteLine(e.Message);
                    return 1;
                }

                var tagNames = await client.GetTagsAsync(tmGame, cancellationToken);
                PrintTrack(track, tagNames);
                mapPath = await client.DownloadMapAsync(tmGame, trackId, cancellationToken);
            }
            else if (File.Exists(value))
            {
                mapPath = value;
            }
            else
            {
                Console.Error.WriteLine($"'{value}' is neither a TMX track id nor an existing file.");
                return 1;
            }

            var map = TmMapReader.Read(mapPath);
            PrintMap(map, mapPath);
            Console.WriteLine();
            Console.WriteLine($"AC track id:  {TrackIds.Create(tmGame, track?.Id ?? 0, track?.Name ?? map.Name)}");
            return 0;
        });
        return command;
    }

    private static void PrintTrack(TmxTrack t, IReadOnlyDictionary<int, string> tagNames)
    {
        var authors = t.Authors.Count > 0 ? string.Join(", ", t.Authors) : t.Uploader;
        var tags = string.Join(", ", t.TagIds.Select(id => tagNames.TryGetValue(id, out var n) ? n : id.ToString(CultureInfo.InvariantCulture)));
        Console.WriteLine($"{t.Game.Id().ToUpperInvariant()}-X #{t.Id}  {t.Name}");
        Console.WriteLine($"  {t.PageUri}");
        Console.WriteLine($"  Author:      {authors}   uploaded {t.UploadedAt:yyyy-MM-dd}   awards {t.Awards}");
        Console.WriteLine($"  Type:        {TmxEnums.PrimaryType(t.PrimaryType)}   mood {TmxEnums.Mood(t.Mood)}   environment {TmxEnums.Environment(t.Environment)}");
        Console.WriteLine($"  Tags:        {(tags.Length > 0 ? tags : "-")}");
        Console.WriteLine($"  Author time: {CliCommon.FormatTime(t.AuthorTimeMs)}   WR {CliCommon.FormatTime(t.WrTimeMs)}{(t.WrReplayId is { } r ? $" (replay {r})" : "")}");
        Console.WriteLine($"  Screenshots: {t.ScreenshotCount}");
        Console.WriteLine();
    }

    private static void PrintMap(TmMap map, string path)
    {
        var waypoints = map.Waypoints.ToList();
        Console.WriteLine($"Map file:     {path}");
        Console.WriteLine($"  Name:       {map.Name}   (uid {map.Uid})");
        Console.WriteLine($"  Author:     {map.Author}   environment {map.Collection}   mood {map.Mood}   author time {CliCommon.FormatTime(map.AuthorTimeMs)}");
        Console.WriteLine($"  Size:       {map.Size.X} x {map.Size.Y} x {map.Size.Z}   {map.Blocks.Count} blocks ({map.Blocks.Count(b => b.IsPillar)} pillar, {map.Blocks.Count(b => b.IsClip)} clip, {map.Blocks.Count(b => b.IsGround)} ground), {map.ItemCount} items");
        Console.WriteLine($"  Race:       {(map.IsMultilap ? $"multilap circuit, {map.Laps} laps" : "point-to-point (A to B)")}");
        Console.WriteLine($"  Waypoints:  {Count(waypoints, TmWaypoint.Start)} start, {Count(waypoints, TmWaypoint.StartFinish)} start/finish, {Count(waypoints, TmWaypoint.Finish)} finish, {Count(waypoints, TmWaypoint.Checkpoint)} checkpoint");
        foreach (var w in waypoints.OrderBy(w => w.Waypoint))
        {
            Console.WriteLine($"    {w.Waypoint,-11} {w.Name,-34} {w.Coord,-14} {w.Direction}{(w.IsGround ? "  (ground)" : "")}");
        }

        var top = map.Blocks.GroupBy(b => b.Name).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} x{g.Count()}");
        Console.WriteLine($"  Top blocks: {string.Join(", ", top)}");

        if (Count(waypoints, TmWaypoint.Start) + Count(waypoints, TmWaypoint.StartFinish) == 0)
        {
            Console.WriteLine("  WARNING: no start block found.");
        }
    }

    private static int Count(List<TmBlock> waypoints, TmWaypoint kind) => waypoints.Count(w => w.Waypoint == kind);
}
