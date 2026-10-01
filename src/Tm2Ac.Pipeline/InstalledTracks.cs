using System.Text.Json.Nodes;
using Tm2Ac.AcTrack;

namespace Tm2Ac.Pipeline;

/// <summary>A track folder written by Tm2Ac (has our conversion-report.json).</summary>
public sealed record InstalledTrack(string TrackId, string Directory, string Name)
{
    public string Game { get; init; } = "";
    public long TmxId { get; init; }
    public DateTime ConvertedAtUtc { get; init; }
    public long SizeBytes { get; init; }
    public string? PreviewPath { get; init; }
}

/// <summary>Finds and removes Tm2Ac conversions in an AC tracks folder. Folders without our marker are never touched.</summary>
public static class InstalledTracks
{
    public static IReadOnlyList<InstalledTrack> Scan(string tracksDirectory)
    {
        if (!Directory.Exists(tracksDirectory))
        {
            return [];
        }

        var result = new List<InstalledTrack>();
        foreach (var directory in Directory.EnumerateDirectories(tracksDirectory))
        {
            var reportPath = Path.Combine(directory, AcTrackWriter.ReportFileName);
            if (!File.Exists(reportPath))
            {
                continue;
            }

            JsonNode? report;
            try
            {
                report = JsonNode.Parse(File.ReadAllText(reportPath));
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }

            if ((string?)report?["generator"] != "Tm2Ac")
            {
                continue;
            }

            var id = Path.GetFileName(directory);
            var name = id;
            var uiPath = Path.Combine(directory, "ui", "ui_track.json");
            if (File.Exists(uiPath))
            {
                try
                {
                    name = (string?)JsonNode.Parse(File.ReadAllText(uiPath))?["name"] ?? id;
                }
                catch (System.Text.Json.JsonException)
                {
                    // keep the folder name
                }
            }

            var source = report?["source"];
            var preview = Path.Combine(directory, "ui", "preview.png");
            result.Add(new InstalledTrack(id, directory, name)
            {
                Game = (string?)source?["game"] ?? "",
                TmxId = source?["tmxId"] is { } tmx ? tmx.GetValue<long>() : 0,
                ConvertedAtUtc = DateTime.TryParse((string?)report?["convertedAtUtc"] ?? (string?)report?["writtenAtUtc"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : Directory.GetLastWriteTimeUtc(directory),
                SizeBytes = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length),
                PreviewPath = File.Exists(preview) ? preview : null,
            });
        }

        return result.OrderByDescending(t => t.ConvertedAtUtc).ToList();
    }

    /// <summary>Deletes a Tm2Ac track folder. Throws if the folder wasn't created by Tm2Ac.</summary>
    public static void Uninstall(InstalledTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!File.Exists(Path.Combine(track.Directory, AcTrackWriter.ReportFileName)))
        {
            throw new IOException($"'{track.Directory}' was not created by Tm2Ac; refusing to delete it.");
        }

        Directory.Delete(track.Directory, recursive: true);
    }
}
