using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Tm2Ac.AcTrack;

namespace Tm2Ac.Pipeline;

/// <summary>
/// TM physics surface → AC surface key, from data/surface-map.tmnf.json. Triangles steeper than
/// <see cref="WallSlopeDegrees"/> become walls whatever their material (AC can't drive walls; loops are flagged Red anyway).
/// </summary>
public sealed class SurfaceMap
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, TmSurfaceDto> _tm;
    private readonly float _wallCos;

    private SurfaceMap(FileDto file)
    {
        _tm = new Dictionary<string, TmSurfaceDto>(file.TmSurfaces, StringComparer.Ordinal);
        WallSlopeDegrees = file.WallSlopeDegrees;
        _wallCos = MathF.Cos(file.WallSlopeDegrees * MathF.PI / 180);
        UnknownSurface = file.UnknownSurface;
        AcSurfaces = file.AcSurfaces.ToDictionary(kv => kv.Key, kv => kv.Value.ToDefinition(kv.Key), StringComparer.Ordinal);
    }

    public static SurfaceMap Tmnf { get; } = Load("surface-map.tmnf.json");

    public float WallSlopeDegrees { get; }
    public string UnknownSurface { get; }

    /// <summary>AC surface definitions by key (WALL is deliberately absent, see <see cref="SurfaceKeys.Wall"/>).</summary>
    public IReadOnlyDictionary<string, SurfaceDefinition> AcSurfaces { get; }

    /// <summary>
    /// AC key for a collision triangle, or null to drop it (non-collidable / water).
    /// <paramref name="issue"/> is the compatibility issue code the surface implies, if any.
    /// </summary>
    public string? Classify(string tmSurface, Vector3 faceNormal, out string? issue)
    {
        issue = null;
        string? key;
        if (_tm.TryGetValue(tmSurface, out var entry))
        {
            key = entry.Ac;
            issue = entry.Issue;
        }
        else
        {
            key = UnknownSurface;
            issue = "UNKNOWN_SURFACE";
        }

        if (key is null)
        {
            return null;
        }

        // Steep or downward-facing faces are walls.
        var n = faceNormal.LengthSquared() > 0 ? Vector3.Normalize(faceNormal) : Vector3.UnitY;
        return MathF.Abs(n.Y) < _wallCos || n.Y < 0 ? SurfaceKeys.Wall : key;
    }

    public static SurfaceMap Parse(string json) => new(JsonSerializer.Deserialize<FileDto>(json, Json) ?? throw new InvalidDataException("Empty surface map."));

    private static SurfaceMap Load(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private sealed record FileDto(float WallSlopeDegrees, Dictionary<string, AcSurfaceDto> AcSurfaces, Dictionary<string, TmSurfaceDto> TmSurfaces, string UnknownSurface);

    private sealed record TmSurfaceDto(string? Ac, string? Issue);

    private sealed record AcSurfaceDto(
        float Friction,
        float DirtAdditive = 0,
        float SinHeight = 0,
        float SinLength = 0,
        string Wav = "",
        bool IsValidTrack = true,
        float VibrationGain = 0,
        float VibrationLength = 0,
        string? ExtSurfaceType = null)
    {
        public SurfaceDefinition ToDefinition(string key) => new(key, Friction)
        {
            DirtAdditive = DirtAdditive,
            SinHeight = SinHeight,
            SinLength = SinLength,
            Wav = Wav,
            IsValidTrack = IsValidTrack,
            VibrationGain = VibrationGain,
            VibrationLength = VibrationLength,
            ExtSurfaceType = ExtSurfaceType,
        };
    }
}
