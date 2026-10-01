using Tm2Ac.Core;

namespace Tm2Ac.Tmx;

/// <summary>Track metadata from TMNF-X / TMUF-X (field reference: docs/research/S3-tmx-api.md).</summary>
public sealed record TmxTrack(TmGame Game, long Id, string Name)
{
    public string Uid { get; init; } = "";
    public string Uploader { get; init; } = "";
    public IReadOnlyList<string> Authors { get; init; } = [];
    public int? AuthorTimeMs { get; init; }
    public IReadOnlyList<int> TagIds { get; init; } = [];
    public int Environment { get; init; }
    public int Mood { get; init; }
    public int PrimaryType { get; init; }
    public int Routes { get; init; }
    public int Difficulty { get; init; }
    public int Awards { get; init; }
    public DateTime UploadedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string Description { get; init; } = "";
    public long? WrReplayId { get; init; }
    public int? WrTimeMs { get; init; }
    public int ScreenshotCount { get; init; }

    public Uri PageUri => new(Game.ExchangeBaseUri(), $"trackshow/{Id}");
}

public sealed record TmxReplay(long Id, int TimeMs, string Player, bool IsBest);

public sealed record TmxSearchPage(IReadOnlyList<TmxTrack> Results, bool More);

public enum TmxTrackOrder
{
    /// <summary>TMX default ordering.</summary>
    Default = 0,
    Newest = 2,
    MostAwards = 6,
    MostRecentActivity = 10,
}

public sealed record TmxSearchQuery
{
    public string? Name { get; init; }
    public string? Author { get; init; }

    /// <summary>Tag ids that must be present (see <see cref="TmxClient.GetTagsAsync"/>).</summary>
    public IReadOnlyList<int> Tags { get; init; } = [];

    /// <summary>Tag ids that must not be present.</summary>
    public IReadOnlyList<int> ExcludedTags { get; init; } = [];

    /// <summary>Optional primary type filter (TMX accepts a single value): Race (0) or Laps (5) are the kinds that make sense in AC.</summary>
    public int? PrimaryType { get; init; }

    public TmxTrackOrder Order { get; init; } = TmxTrackOrder.Default;
    public int Count { get; init; } = 20;

    /// <summary>Cursor for the next page: the last track id of the previous page.</summary>
    public long? After { get; init; }
}

/// <summary>Display names for TMX enum values (from https://api.mania.exchange/api/enums/{id}).</summary>
public static class TmxEnums
{
    public const int PrimaryTypeRace = 0;
    public const int PrimaryTypeLaps = 5;

    public static string Mood(int value) => value switch
    {
        0 => "Sunrise",
        1 => "Day",
        2 => "Sunset",
        3 => "Night",
        _ => $"Mood {value}",
    };

    public static string PrimaryType(int value) => value switch
    {
        0 => "Race",
        1 => "Puzzle",
        2 => "Platform",
        3 => "Stunts",
        4 => "Shortcut",
        5 => "Laps",
        _ => $"Type {value}",
    };

    /// <summary>TMNF-X / TMUF-X environments.</summary>
    public static string Environment(int value) => value switch
    {
        1 => "Snow",
        2 => "Desert",
        3 => "Rally",
        4 => "Island",
        5 => "Coast",
        6 => "Bay",
        7 => "Stadium",
        _ => $"Environment {value}",
    };
}

public sealed class TmxNotFoundException(string message) : Exception(message);
