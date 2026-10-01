using System.CommandLine;
using System.Text.Json.Nodes;
using Tm2Ac.AcTrack;
using Tm2Ac.AcTrack.Synthetic;
using Tm2Ac.Core;

namespace Tm2Ac.Cli;

internal static class DevCommands
{
    public static Command Create()
    {
        var dev = new Command("dev", "Developer tools.");

        var outOption = new Option<DirectoryInfo?>("--out") { Description = "Write the track folder under this directory instead of AC's content/tracks." };
        var acPathOption = new Option<DirectoryInfo?>("--ac-path") { Description = "Assetto Corsa install folder (auto-detected from Steam when omitted)." };
        var testTrack = new Command("test-track", "Generate the Phase 1 synthetic L-shaped test circuit and install it into Assetto Corsa.");
        testTrack.Options.Add(outOption);
        testTrack.Options.Add(acPathOption);
        testTrack.SetAction(parseResult => WriteTestTrack(parseResult.GetValue(outOption), parseResult.GetValue(acPathOption)));

        dev.Subcommands.Add(testTrack);
        return dev;
    }

    private static int WriteTestTrack(DirectoryInfo? outDirectory, DirectoryInfo? acPath)
    {
        string tracksDirectory;
        if (outDirectory is not null)
        {
            tracksDirectory = outDirectory.FullName;
        }
        else
        {
            var acRoot = acPath?.FullName ?? (OperatingSystem.IsWindows() ? AssettoCorsa.FindInstall() : null);
            if (acRoot is null)
            {
                Console.Error.WriteLine("Assetto Corsa not found. Pass --ac-path <AC folder> or --out <folder>.");
                return 1;
            }

            var csp = AssettoCorsa.FindCspVersion(acRoot);
            Console.WriteLine($"Assetto Corsa: {acRoot} (CSP {csp ?? "not installed"})");
            tracksDirectory = AssettoCorsa.TracksDirectory(acRoot);
        }

        var track = TestCircuit.Build();
        var trackDirectory = Path.Combine(tracksDirectory, track.Id);
        var report = new JsonObject { ["source"] = "synthetic:test-circuit", ["phase"] = 1 };
        AcTrackWriter.Write(track, trackDirectory, report);

        Console.WriteLine($"Wrote {track.Ui.Name} to {trackDirectory}");
        Console.WriteLine($"  length {track.MapPath.Length:0} m, {track.VisualMeshes.Count} visual meshes, {track.CollisionMeshes.Count} collision meshes, {track.Dummies.Count} dummies");
        return 0;
    }
}
