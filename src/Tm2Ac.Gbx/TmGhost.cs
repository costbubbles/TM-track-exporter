using System.Numerics;
using GBX.NET.Engines.Game;
using GBX.NET.Engines.Scene;

namespace Tm2Ac.Gbx;

/// <summary>A recorded run: position samples (TM world metres) plus checkpoint times. See docs/research/S4-ghost-parsing.md.</summary>
public sealed record TmGhost(int RaceTimeMs, IReadOnlyList<int> CheckpointTimesMs, IReadOnlyList<TmGhostSample> Samples, int SamplePeriodMs)
{
    public string Player { get; init; } = "";

    /// <summary>Sample nearest to <paramref name="timeMs"/> (indexed by period; per-sample times are unreliable).</summary>
    public TmGhostSample At(int timeMs) => Samples[Math.Clamp((int)Math.Round((double)timeMs / SamplePeriodMs), 0, Samples.Count - 1)];
}

/// <param name="WheelsOnGround">0–4 wheels touching a surface.</param>
/// <param name="Up">The car's local up axis in world space (negative Y = upside down).</param>
/// <param name="Surface">Ground contact material of the front-left wheel (e.g. "Asphalt").</param>
public sealed record TmGhostSample(int TimeMs, Vector3 Position, Vector3 Velocity, Vector3 Up, int WheelsOnGround, string Surface);

public static class TmGhostReader
{
    static TmGhostReader() => GbxSetup.EnsureInitialized();

    /// <summary>Reads the first ghost of a replay (.Replay.Gbx) or a standalone ghost (.Ghost.Gbx).</summary>
    public static TmGhost Read(string path)
    {
        var node = GBX.NET.Gbx.ParseNode(path);
        var ghost = node switch
        {
            CGameCtnReplayRecord replay => replay.Ghosts?.FirstOrDefault(),
            CGameCtnGhost g => g,
            _ => null,
        } ?? throw new InvalidDataException($"{path} contains no ghost.");

        var data = ghost.SampleData ?? throw new InvalidDataException($"{path}: ghost has no sample data (unsupported ghost format).");
        var period = Math.Max(1, (int)data.SamplePeriod.TotalMilliseconds);
        var samples = new List<TmGhostSample>(data.Samples.Count);
        for (var i = 0; i < data.Samples.Count; i++)
        {
            var s = data.Samples[i];
            var rotation = new Quaternion(s.Rotation.X, s.Rotation.Y, s.Rotation.Z, s.Rotation.W);
            var up = Vector3.Transform(Vector3.UnitY, rotation);
            var (wheels, surface) = s is CSceneVehicleCar.Sample car
                ? ((car.FLOnGround ? 1 : 0) + (car.FROnGround ? 1 : 0) + (car.RLOnGround ? 1 : 0) + (car.RROnGround ? 1 : 0), car.FLGroundContactMaterial.ToString())
                : (4, "");
            samples.Add(new TmGhostSample(i * period, new Vector3(s.Position.X, s.Position.Y, s.Position.Z), new Vector3(s.Velocity.X, s.Velocity.Y, s.Velocity.Z), up, wheels, surface));
        }

        var checkpoints = (ghost.Checkpoints ?? []).Select(c => (int)(c.Time?.TotalMilliseconds ?? 0)).ToList();
        var raceTime = ghost.RaceTime is { } t ? (int)t.TotalMilliseconds : checkpoints.LastOrDefault();
        return new TmGhost(raceTime, checkpoints, samples, period) { Player = Core.TmText.StripFormatting(ghost.GhostNickname) };
    }
}
