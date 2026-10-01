namespace Tm2Ac.Core;

/// <summary>Trackmania games we can read from, and their Trackmania Exchange site.</summary>
public enum TmGame
{
    Tmnf,
    Tmuf,
    Tm2020,
}

public static class TmGames
{
    /// <summary>Short id used in CLI arguments and track folder names ("tmnf", "tmuf", "tm2020").</summary>
    public static string Id(this TmGame game) => game switch
    {
        TmGame.Tmnf => "tmnf",
        TmGame.Tmuf => "tmuf",
        TmGame.Tm2020 => "tm2020",
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };

    public static bool TryParse(string value, out TmGame game)
    {
        foreach (var candidate in Enum.GetValues<TmGame>())
        {
            if (string.Equals(candidate.Id(), value, StringComparison.OrdinalIgnoreCase))
            {
                game = candidate;
                return true;
            }
        }

        game = default;
        return false;
    }

    /// <summary>The game's Trackmania Exchange site.</summary>
    public static Uri ExchangeBaseUri(this TmGame game) => game switch
    {
        TmGame.Tmnf => new Uri("https://tmnf.exchange/"),
        TmGame.Tmuf => new Uri("https://tmuf.exchange/"),
        TmGame.Tm2020 => new Uri("https://trackmania.exchange/"),
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };
}
