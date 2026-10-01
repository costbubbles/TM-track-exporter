namespace Tm2Ac.Core.Tests;

public class TmTextTests
{
    [Theory]
    [InlineData("$s$w$666P$888h$aaa/\\$cccn$ffftom $aaaFa$888k$666e", "Ph/\\ntom Fake")] // real TMNF-X #924307 map name
    [InlineData("$f00Red $0f0Green", "Red Green")]
    [InlineData("$oBold$z $iItalic", "Bold Italic")]
    [InlineData("Price: $$5", "Price: $5")]
    [InlineData("$l[https://example.com]Link$l text", "Link text")]
    [InlineData("$h[manialink]Help$h", "Help")]
    [InlineData("$F0aMixed$<inner$>", "Mixedinner")]
    [InlineData("  plain  ", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void StripsFormattingCodes(string? input, string expected) => Assert.Equal(expected, TmText.StripFormatting(input));

    [Theory]
    [InlineData("Dirty Dreams...", "dirty_dreams")]
    [InlineData("TWC 2023 /// Copenhagen", "twc_2023_copenhagen")]
    [InlineData("Ünïcödé Çàfé", "unicode_cafe")]
    [InlineData("»shining Dirt", "shining_dirt")]
    [InlineData("!!!", "")]
    public void SlugifiesToLowerAscii(string input, string expected) => Assert.Equal(expected, TmText.Slugify(input, 64));

    [Fact]
    public void TrackIdHasGamePrefixAndRespectsMaxLength()
    {
        Assert.Equal("tmnf_924307_pf_ph_ntom_fake", TrackIds.Create(TmGame.Tmnf, 924307, "[PF] Ph/\\ntom Fake"));
        Assert.Equal("tmnf_5", TrackIds.Create(TmGame.Tmnf, 5, "$f00!!!"));

        var longId = TrackIds.Create(TmGame.Tmuf, 123456789, new string('a', 200));
        Assert.True(longId.Length <= TrackIds.MaxLength);
        Assert.StartsWith("tmuf_123456789_aaa", longId, StringComparison.Ordinal);
        Assert.DoesNotMatch("[^a-z0-9_]", longId);
    }

    [Theory]
    [InlineData("tmnf", TmGame.Tmnf)]
    [InlineData("TMUF", TmGame.Tmuf)]
    [InlineData("tm2020", TmGame.Tm2020)]
    public void ParsesGameIds(string value, TmGame expected)
    {
        Assert.True(TmGames.TryParse(value, out var game));
        Assert.Equal(expected, game);
    }
}
