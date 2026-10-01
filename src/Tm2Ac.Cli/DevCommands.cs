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
        dev.Subcommands.Add(RenderCommand.Create());
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

internal static class RenderCommand
{
    /// <summary>
    /// Top-down debug render of a converted track: collision triangles coloured by surface, AC_* dummies as markers.
    /// Lets geometry be checked without starting AC.
    /// </summary>
    public static Command Create()
    {
        var dir = new Argument<DirectoryInfo>("track-folder") { Description = "A converted track folder (contains collision.kn5)." };
        var output = new Argument<FileInfo>("png") { Description = "Output image." };
        var size = new Option<int>("--size") { Description = "Image size in pixels.", DefaultValueFactory = _ => 1600 };
        var command = new Command("render", "Render a top-down debug image of a track's collision and dummies.") { dir, output, size };
        command.SetAction(result =>
        {
            var folder = result.GetValue(dir)!.FullName;
            var collision = Kn5.Kn5Reader.Read(Path.Combine(folder, "collision.kn5"));
            var visualFile = Directory.GetFiles(folder, "*.kn5").First(f => !f.EndsWith("collision.kn5", StringComparison.OrdinalIgnoreCase));
            var visual = Kn5.Kn5Reader.Read(visualFile);
            var meshes = collision.Root.Children.OfType<Kn5.Kn5MeshNode>().ToList();
            var dummies = visual.Root.Children.OfType<Kn5.Kn5DummyNode>().ToList();
            var all = meshes.SelectMany(m => m.Vertices).Select(v => v.Position).ToList();
            var walls = meshes.Where(m => m.Name.Contains("WALL", StringComparison.Ordinal)).SelectMany(m => m.Vertices).Select(v => v.Position).ToHashSet();
            var bounds = all.Where(p => !walls.Contains(p)).DefaultIfEmpty(System.Numerics.Vector3.Zero).ToList();
            float minX = bounds.Min(p => p.X), maxX = bounds.Max(p => p.X), minZ = bounds.Min(p => p.Z), maxZ = bounds.Max(p => p.Z);
            var px = result.GetValue(size);
            var scale = (px - 40) / Math.Max(maxX - minX, maxZ - minZ);
            SkiaSharp.SKPoint P(System.Numerics.Vector3 v) => new(20 + ((v.X - minX) * scale), 20 + ((v.Z - minZ) * scale));

            using var bitmap = new SkiaSharp.SKBitmap(px, px);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(new SkiaSharp.SKColor(0x18, 0x18, 0x18));
            var colors = new Dictionary<string, SkiaSharp.SKColor>
            {
                ["ROAD"] = new(0x9A, 0x9A, 0xA0), ["GRASS"] = new(0x3C, 0x7A, 0x30), ["DIRT"] = new(0x9C, 0x6B, 0x3A), ["ICE"] = new(0x9C, 0xDC, 0xF0), ["WALL"] = new(0xC0, 0x40, 0x40),
            };

            // Draw low surfaces first so elevated roads sit on top.
            foreach (var mesh in meshes.OrderBy(m => m.Name.Contains("GRASS", StringComparison.Ordinal) ? 0 : 1).ThenBy(m => m.BoundingSphereCenter.Y))
            {
                var key = colors.Keys.FirstOrDefault(k => mesh.Name.Contains(k, StringComparison.Ordinal)) ?? "ROAD";
                using var paint = new SkiaSharp.SKPaint { Color = colors[key].WithAlpha(key == "WALL" ? (byte)200 : (byte)255), IsAntialias = false };
                for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
                {
                    using var path = new SkiaSharp.SKPathBuilder();
                    path.MoveTo(P(mesh.Vertices[mesh.Indices[t]].Position));
                    path.LineTo(P(mesh.Vertices[mesh.Indices[t + 1]].Position));
                    path.LineTo(P(mesh.Vertices[mesh.Indices[t + 2]].Position));
                    path.Close();
                    using var sk = path.Detach();
                    canvas.DrawPath(sk, paint);
                }
            }

            using var marker = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Yellow, IsAntialias = true, StrokeWidth = 3 };
            foreach (var d in dummies)
            {
                var at = P(d.Transform.Translation);
                var forward = new System.Numerics.Vector3(d.Transform.M31, 0, d.Transform.M33);
                canvas.DrawCircle(at, 5, marker);
                canvas.DrawLine(at, P(d.Transform.Translation + (forward * 12 / scale * 4)), marker);
            }

            using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(result.GetValue(output)!.FullName, data.ToArray());
            Console.WriteLine($"Rendered {meshes.Count} collision meshes and {dummies.Count} dummies to {result.GetValue(output)!.FullName}");
            return 0;
        });
        return command;
    }
}
