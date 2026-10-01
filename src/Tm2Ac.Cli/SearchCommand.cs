using System.CommandLine;
using Tm2Ac.Tmx;

namespace Tm2Ac.Cli;

internal static class SearchCommand
{
    public static Command Create()
    {
        var game = CliCommon.GameArgument();
        var name = new Argument<string?>("query") { Description = "Track name to search for.", Arity = ArgumentArity.ZeroOrOne };
        var author = new Option<string?>("--author") { Description = "Author name." };
        var tags = new Option<string[]>("--tag") { Description = "Required tag (repeatable), e.g. Tech, Dirt, Multilap.", AllowMultipleArgumentsPerToken = true };
        var limit = new Option<int>("--limit") { Description = "Number of results (max 100).", DefaultValueFactory = _ => 20 };
        var order = new Option<TmxTrackOrder>("--order") { Description = "Sort order.", DefaultValueFactory = _ => TmxTrackOrder.MostAwards };
        var laps = new Option<bool>("--multilap") { Description = "Only multilap circuits (TMX tag Multilap)." };

        var command = new Command("search", "Search Trackmania Exchange (Race-type tracks).") { game, name, author, tags, limit, order, laps };
        command.SetAction(async (result, cancellationToken) =>
        {
            if (!CliCommon.TryGetGame(result.GetValue(game)!, out var tmGame))
            {
                return 1;
            }

            using var client = TmxClient.CreateDefault();
            var tagNames = await client.GetTagsAsync(tmGame, cancellationToken);
            var tagIds = new List<int>();
            foreach (var tag in result.GetValue(tags) ?? [])
            {
                var match = tagNames.FirstOrDefault(t => string.Equals(t.Value, tag, StringComparison.OrdinalIgnoreCase));
                if (match.Value is null)
                {
                    Console.Error.WriteLine($"Unknown tag '{tag}'. Known tags: {string.Join(", ", tagNames.Values.Order())}");
                    return 1;
                }

                tagIds.Add(match.Key);
            }

            if (result.GetValue(laps))
            {
                tagIds.Add(tagNames.First(t => t.Value == "Multilap").Key);
            }

            var page = await client.SearchTracksAsync(tmGame, new TmxSearchQuery
            {
                Name = result.GetValue(name),
                Author = result.GetValue(author),
                Tags = tagIds,
                Count = Math.Clamp(result.GetValue(limit), 1, 100),
                Order = result.GetValue(order),
                PrimaryType = TmxEnums.PrimaryTypeRace,
            }, cancellationToken);

            Console.WriteLine($"{"ID",-9} {"Name",-34} {"Author",-20} {"Awards",6} {"WR",10}  Tags");
            foreach (var t in page.Results)
            {
                var tagText = string.Join(",", t.TagIds.Select(id => tagNames.TryGetValue(id, out var n) ? n : id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                Console.WriteLine($"{t.Id,-9} {CliCommon.Truncate(t.Name, 34),-34} {CliCommon.Truncate(t.Authors.Count > 0 ? t.Authors[0] : t.Uploader, 20),-20} {t.Awards,6} {CliCommon.FormatTime(t.WrTimeMs),10}  {tagText}");
            }

            if (page.More)
            {
                Console.WriteLine("(more results available: refine the query or raise --limit)");
            }

            return 0;
        });
        return command;
    }
}
