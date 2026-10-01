using Tm2Ac.AcTrack.Ini;

namespace Tm2Ac.AcTrack;

/// <summary>
/// One <c>[SURFACE_n]</c> entry. Collision meshes named <c>&lt;digits&gt;&lt;Key&gt;&lt;digits&gt;</c> use it; digit-prefixed
/// meshes whose key isn't defined (e.g. <see cref="SurfaceKeys.Wall"/>) are treated as walls by AC.
/// </summary>
public sealed record SurfaceDefinition(string Key, float Friction)
{
    public float Damping { get; init; }
    public string Wav { get; init; } = "";
    public float WavPitch { get; init; }
    public string FfEffect { get; init; } = "NULL";
    public float DirtAdditive { get; init; }
    public float BlackFlagTime { get; init; }
    public bool IsValidTrack { get; init; } = true;
    public float SinHeight { get; init; }
    public float SinLength { get; init; }
    public bool IsPitlane { get; init; }
    public float VibrationGain { get; init; }
    public float VibrationLength { get; init; }

    /// <summary>CSP <c>_EXT_SURFACE_TYPE</c> (GRASS, SAND, ICE, SNOW, GRAVEL, KERB, EXTRATURF, OLD). Needs extended surfaces enabled.</summary>
    public string? ExtSurfaceType { get; init; }
}

public static class SurfaceKeys
{
    public const string Road = "ROAD";
    public const string Grass = "GRASS";
    public const string Kerb = "KERB";
    public const string Sand = "SAND";

    /// <summary>Not defined in surfaces.ini on purpose: AC treats undefined digit-prefixed meshes as walls.</summary>
    public const string Wall = "WALL";
}

/// <summary>Defaults copied from AC's own system/data/surfaces.ini.</summary>
public static class StandardSurfaces
{
    public static SurfaceDefinition Road { get; } = new(SurfaceKeys.Road, 1.0f);

    public static SurfaceDefinition Grass { get; } = new(SurfaceKeys.Grass, 0.6f)
    {
        Wav = "grass.wav",
        DirtAdditive = 1,
        IsValidTrack = false,
        SinHeight = 0.03f,
        SinLength = 0.5f,
        VibrationGain = 0.2f,
        VibrationLength = 0.6f,
    };

    public static SurfaceDefinition Kerb { get; } = new(SurfaceKeys.Kerb, 0.92f)
    {
        Wav = "kerb.wav",
        WavPitch = 1.3f,
        FfEffect = "1",
        VibrationGain = 1.0f,
        VibrationLength = 1.5f,
    };

    public static SurfaceDefinition Sand { get; } = new(SurfaceKeys.Sand, 0.8f)
    {
        Damping = 0.1f,
        Wav = "sand.wav",
        FfEffect = "0",
        DirtAdditive = 1,
        IsValidTrack = false,
        SinHeight = 0.04f,
        SinLength = 0.5f,
        VibrationGain = 0.2f,
        VibrationLength = 0.3f,
    };
}

public static class SurfacesIni
{
    /// <param name="extendedPhysics">Marks the track for CSP extended physics (<c>WAV_PITCH=extended-0</c> on the first surface), required for <c>_EXT_*</c> keys.</param>
    public static IniFile Build(IReadOnlyList<SurfaceDefinition> surfaces, bool extendedPhysics)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        var ini = new IniFile();
        for (var i = 0; i < surfaces.Count; i++)
        {
            var s = surfaces[i];
            var section = ini.Section($"SURFACE_{i}")
                .Set("KEY", s.Key)
                .Set("FRICTION", s.Friction)
                .Set("DAMPING", s.Damping)
                .Set("WAV", s.Wav)
                .Set("WAV_PITCH", i == 0 && extendedPhysics ? "extended-0" : IniFile.Format(s.WavPitch))
                .Set("FF_EFFECT", s.FfEffect)
                .Set("DIRT_ADDITIVE", s.DirtAdditive)
                .Set("BLACK_FLAG_TIME", s.BlackFlagTime)
                .Set("IS_VALID_TRACK", s.IsValidTrack)
                .Set("SIN_HEIGHT", s.SinHeight)
                .Set("SIN_LENGTH", s.SinLength)
                .Set("IS_PITLANE", s.IsPitlane)
                .Set("VIBRATION_GAIN", s.VibrationGain)
                .Set("VIBRATION_LENGTH", s.VibrationLength);

            if (extendedPhysics && s.ExtSurfaceType is not null)
            {
                section.Set("_EXT_SURFACE_TYPE", s.ExtSurfaceType);
            }
        }

        return ini;
    }
}
