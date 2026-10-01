using System.CommandLine;
using Tm2Ac.Assets;
using Tm2Ac.Core;
using Tm2Ac.Gbx;
using Tm2Ac.Tmx;

namespace Tm2Ac.Cli;

internal static class AssetsCommand
{
    public static Command Create()
    {
        var assets = new Command("assets", "Inspect the Trackmania game assets used for conversion.");

        var maps = new Argument<long[]>("tmx-ids") { Description = "TMNF-X track ids whose blocks should be checked.", Arity = ArgumentArity.ZeroOrMore };
        var check = new Command("check", "Extract every TMNF Stadium block and report what can't be converted.") { maps };
        check.SetAction(async (result, cancellationToken) =>
        {
            var tmnf = CliCommon.FindTmnf();
            if (tmnf is null)
            {
                return 1;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var library = TmnfBlockLibrary.Open(tmnf);
            var noGeometry = new List<string>();
            var triangles = 0L;
            foreach (var name in library.BlockNames.Order(StringComparer.Ordinal))
            {
                var block = library.Get(name);
                var count = block.Variants.Sum(v => v.Parts.Sum(p => p.Mesh.TriangleCount));
                triangles += count;
                if (count == 0)
                {
                    noGeometry.Add(name);
                }
            }

            Console.WriteLine($"TMNF assets: {tmnf} (build {library.FileSystem.BuildKey})");
            Console.WriteLine($"  {library.BlockNames.Count} blocks extracted in {sw.Elapsed.TotalSeconds:0.0} s, {triangles:N0} visual triangles, {library.Materials.Count} materials");
            Console.WriteLine($"  {noGeometry.Count} blocks without visual geometry: {string.Join(", ", noGeometry)}");
            Console.WriteLine($"  {library.FileSystem.FailedFiles.Count} game files GBX.NET could not parse");

            var ids = result.GetValue(maps) ?? [];
            if (ids.Length > 0)
            {
                using var client = TmxClient.CreateDefault();
                foreach (var id in ids)
                {
                    var map = TmMapReader.Read(await client.DownloadMapAsync(TmGame.Tmnf, id, cancellationToken));
                    var missing = map.Blocks.Where(b => library.GetVariant(b.Name, b.IsGround, b.Variant, b.SubVariant) is null)
                        .GroupBy(b => b.Name).Select(g => $"{g.Key} x{g.Count()}").ToList();
                    Console.WriteLine($"  #{id} {map.Name}: {map.Blocks.Count} blocks, {(missing.Count == 0 ? "all have geometry" : $"MISSING {string.Join(", ", missing)}")}");
                }
            }

            return 0;
        });

        assets.Subcommands.Add(check);
        return assets;
    }
}
