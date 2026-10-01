using System.CommandLine;
using System.Globalization;
using Tm2Ac.Assets;
using Tm2Ac.Core;
using Tm2Ac.Pipeline;
using Tm2Ac.Tmx;

namespace Tm2Ac.Cli;

internal static class ConvertCommand
{
    public static Command Create()
    {
        var game = CliCommon.GameArgument();
        var target = new Argument<string>("track") { Description = "TMX track id, or a path to a .Challenge.Gbx file." };
        var outOption = new Option<DirectoryInfo?>("--out") { Description = "Write the track folder under this directory instead of AC's content/tracks." };
        var acPath = new Option<DirectoryInfo?>("--ac-path") { Description = "Assetto Corsa install folder (auto-detected when omitted)." };
        var scale = new Option<float>("--scale") { Description = "Uniform world scale, 0.25–4 (e.g. 1.5 for road cars, 2 for GT).", DefaultValueFactory = _ => 1f };
        var replay = new Option<string?>("--replay") { Description = "TMX replay id or a local .Replay.Gbx/.Ghost.Gbx for the AI line and checkpoint order (default: TMX world record)." };
        var pitboxes = new Option<int>("--pitboxes") { Description = "Grid slots and pit boxes. Default 1: everyone starts at the start line; more adds a grid behind it.", DefaultValueFactory = _ => 1 };
        var noGrass = new Option<bool>("--no-grass") { Description = "Don't fill empty ground cells with the default Stadium grass." };
        var force = new Option<bool>("--force") { Description = "Convert even if the map is rated Red (loops, wall riding, huge jumps)." };
        var zip = new Option<FileInfo?>("--zip") { Description = "Also write a zip that Content Manager can install by drag and drop." };

        var command = new Command("convert", "Convert a Trackmania map into an Assetto Corsa track and install it.") { game, target, outOption, acPath, scale, replay, pitboxes, noGrass, force, zip };
        command.SetAction(async (result, cancellationToken) =>
        {
            if (!CliCommon.TryGetGame(result.GetValue(game)!, out var tmGame))
            {
                return 1;
            }

            if (tmGame != TmGame.Tmnf)
            {
                Console.Error.WriteLine("Only tmnf maps can be converted so far.");
                return 1;
            }

            var scaleValue = result.GetValue(scale);
            if (scaleValue is < 0.25f or > 4f)
            {
                Console.Error.WriteLine("--scale must be between 0.25 and 4.");
                return 1;
            }

            var tracksDirectory = CliCommon.ResolveTracksDirectory(result.GetValue(outOption), result.GetValue(acPath));
            var tmnf = CliCommon.FindTmnf();
            if (tracksDirectory is null || tmnf is null)
            {
                return 1;
            }

            var value = result.GetValue(target)!;
            var replayValue = result.GetValue(replay);
            using var client = TmxClient.CreateDefault();
            ConversionSource source;
            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var tmxId))
            {
                long? replayId = long.TryParse(replayValue, NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : null;
                Console.WriteLine($"Fetching {tmGame.Id()} #{tmxId} from TMX...");
                source = await ConversionSource.FromTmxAsync(client, tmGame, tmxId, replayId, cancellationToken);
                if (replayValue is not null && replayId is null)
                {
                    source = source with { GhostPath = replayValue };
                }
            }
            else if (File.Exists(value))
            {
                source = new ConversionSource(tmGame, 0, value) { GhostPath = replayValue };
            }
            else
            {
                Console.Error.WriteLine($"'{value}' is neither a TMX track id nor an existing file.");
                return 1;
            }

            Console.WriteLine("Extracting blocks from TMNF and building the track...");
            using var library = TmnfBlockLibrary.Open(tmnf);
            var options = new ConversionOptions { Scale = scaleValue, Pitboxes = result.GetValue(pitboxes), DefaultGrass = !result.GetValue(noGrass), Force = result.GetValue(force) };
            var conversion = new TmnfConverter(library).Convert(source, options, tracksDirectory, new Progress<string>(step => Console.WriteLine($"  {step}...")), cancellationToken);

            if (conversion.Refused)
            {
                Console.WriteLine($"Not converted: rated {conversion.Rating}. The route needs things Assetto Corsa can't do:");
                foreach (var issue in conversion.Issues.Where(i => i.Severity == IssueSeverity.Block))
                {
                    Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
                }

                Console.WriteLine("Use --force to convert anyway.");
                return 2;
            }

            Console.WriteLine($"Wrote {conversion.TrackId} to {conversion.Directory}  (compatibility: {conversion.Rating})");
            Console.WriteLine($"  {conversion.Blocks} blocks, {conversion.VisualTriangles:N0} visual / {conversion.CollisionTriangles:N0} collision triangles, {conversion.Elapsed.TotalSeconds:0.0} s");
            if (result.GetValue(zip) is { } zipFile)
            {
                File.Delete(zipFile.FullName);
                System.IO.Compression.ZipFile.CreateFromDirectory(conversion.Directory, zipFile.FullName, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: true);
                Console.WriteLine($"  zip: {zipFile.FullName}");
            }

            foreach (var issue in conversion.Issues)
            {
                Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
            }

            return 0;
        });
        return command;
    }
}
