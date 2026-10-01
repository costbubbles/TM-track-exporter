using System.CommandLine;
using System.Globalization;
using Tm2Ac.Core;
using Tm2Ac.Gbx;
using Tm2Ac.Pipeline;
using Tm2Ac.Tmx;

namespace Tm2Ac.Cli;

internal static class AnalyzeCommand
{
    public static Command Create()
    {
        var game = CliCommon.GameArgument();
        var target = new Argument<string>("track") { Description = "TMX track id, or a path to a .Challenge.Gbx file." };
        var replay = new Option<string?>("--replay") { Description = "TMX replay id or a local .Replay.Gbx (default: TMX world record)." };
        var command = new Command("analyze", "Rate how well a map will work in Assetto Corsa (Green/Yellow/Red) without converting it.") { game, target, replay };
        command.SetAction(async (result, cancellationToken) =>
        {
            if (!CliCommon.TryGetGame(result.GetValue(game)!, out var tmGame))
            {
                return 1;
            }

            var value = result.GetValue(target)!;
            var replayValue = result.GetValue(replay);
            using var client = TmxClient.CreateDefault();
            string mapPath;
            var ghostPath = replayValue is not null && File.Exists(replayValue) ? replayValue : null;
            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var tmxId))
            {
                var track = await client.GetTrackAsync(tmGame, tmxId, cancellationToken);
                mapPath = await client.DownloadMapAsync(tmGame, tmxId, cancellationToken);
                var replayId = long.TryParse(replayValue, NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : track.WrReplayId;
                if (ghostPath is null && replayId is { } id)
                {
                    ghostPath = await client.DownloadReplayAsync(tmGame, id, cancellationToken);
                }

                Console.WriteLine($"{track.Name}  ({track.PageUri})");
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
            var ghost = ghostPath is null ? null : TmGhostReader.Read(ghostPath);
            var issues = new IssueList();
            var stats = CompatibilityAnalyzer.Analyze(map, ghost, issues);
            var list = issues.ToList();

            Console.WriteLine($"Compatibility: {CompatibilityAnalyzer.Rate(list)}");
            Console.WriteLine(stats is null
                ? "  no replay: the rating is based on the map's blocks only"
                : $"  replay {CliCommon.FormatTime(stats.RaceTimeMs)}: upside down {stats.UpsideDownMs / 1000.0:0.0} s, on walls {stats.WallDrivingMs / 1000.0:0.0} s, longest flight {stats.LongestAirMs / 1000.0:0.0} s, airborne {stats.AirbornePercent}%, on boosters {stats.BoosterContactMs / 1000.0:0.0} s");

            foreach (var issue in list)
            {
                Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
            }

            Console.WriteLine("  (surface approximations and missing blocks are only known after conversion: see conversion-report.json)");
            return 0;
        });
        return command;
    }
}
