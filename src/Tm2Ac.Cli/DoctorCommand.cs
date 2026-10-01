using System.CommandLine;
using Tm2Ac.Core;

namespace Tm2Ac.Cli;

internal static class DoctorCommand
{
    public static Command Create()
    {
        var command = new Command("doctor", "Check Assetto Corsa, CSP and Trackmania installs and the cache.");
        command.SetAction(_ =>
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Tm2Ac only runs on Windows.");
                return 1;
            }

            var ok = true;

            var ac = AssettoCorsa.FindInstall();
            ok &= Report("Assetto Corsa", ac, ac is null ? "not found (Steam app 244210)" : null);
            if (ac is not null)
            {
                var csp = AssettoCorsa.FindCspVersion(ac);
                ok &= Report("Custom Shaders Patch", csp, csp is null ? "not installed: converted tracks require CSP" : null);
            }

            var tmnf = TrackmaniaInstalls.FindTmnf();
            var stadiumPak = tmnf is null ? null : Path.Combine(tmnf, "Packs", "Stadium.pak");
            ok &= Report("TMNF", tmnf, tmnf is null ? "not found: install TrackMania Nations Forever (free on Steam, app 11020)" : null);
            if (stadiumPak is not null)
            {
                ok &= Report("  Stadium.pak", File.Exists(stadiumPak) ? stadiumPak : null, "missing: verify the game files in Steam");
                var textures = Path.Combine(tmnf!, "GameData", "Stadium", "Media", "Texture", "Image");
                ok &= Report("  Stadium textures", Directory.Exists(textures) ? $"{Directory.GetFiles(textures, "*.dds").Length} DDS files" : null, "missing: verify the game files in Steam");
            }

            var tm2020 = TrackmaniaInstalls.FindTm2020();
            Report("Trackmania (2020)", tm2020, tm2020 is null ? "not found (optional until TM2020 support)" : null, required: false);
            if (tm2020 is not null)
            {
                Report("  Openplanet", File.Exists(Path.Combine(tm2020, "Openplanet.dll")) ? "installed" : null, "not installed (optional)", required: false);
            }

            var cache = Tm2AcPaths.CacheDirectory;
            var size = Directory.Exists(cache) ? new DirectoryInfo(cache).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
            Report("Cache", $"{cache} ({size / 1024.0 / 1024.0:0.0} MB)", null);

            Console.WriteLine();
            Console.WriteLine(ok ? "All required components found." : "Some required components are missing (see above).");
            return ok ? 0 : 1;
        });
        return command;
    }

    private static bool Report(string label, string? value, string? problem, bool required = true)
    {
        var mark = value is not null ? "ok  " : required ? "FAIL" : "--  ";
        Console.WriteLine($"[{mark}] {label,-22} {value ?? problem}");
        return value is not null || !required;
    }
}
