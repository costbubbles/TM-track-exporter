using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tm2Ac.Core;

/// <summary>Helpers for Trackmania-formatted text (map names, nicknames, descriptions).</summary>
public static partial class TmText
{
    /// <summary>
    /// Removes Trackmania formatting codes: colours (<c>$f00</c>, <c>$fff</c>; 1–3 hex digits), styles
    /// (<c>$o $i $w $n $t $s $g $z $m $&lt; $&gt;</c>), links (<c>$l[url]</c>, <c>$h[...]</c>, <c>$p[...]</c>) and turns
    /// <c>$$</c> into a literal <c>$</c>. Surrounding whitespace is trimmed.
    /// </summary>
    public static string StripFormatting(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var stripped = FormattingRegex().Replace(text, m => m.Value == "$$" ? "$" : "");
        return stripped.Trim();
    }

    [GeneratedRegex(@"\$\$|\$[lhp](\[[^\]]*\])?|\$[0-9a-fA-F]{1,3}|\$[oiwntsgzmOIWNTSGZM<>]|\$", RegexOptions.CultureInvariant)]
    private static partial Regex FormattingRegex();

    /// <summary>Lower-case ASCII slug: accents removed, runs of other characters become a single underscore.</summary>
    public static string Slugify(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasSeparator = true;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                sb.Append('_');
                lastWasSeparator = true;
            }
        }

        var slug = sb.ToString().TrimEnd('_');
        return slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('_');
    }
}

/// <summary>AC track folder ids: <c>tm&lt;game&gt;_&lt;tmxId&gt;_&lt;slug&gt;</c>, lower-case ASCII, at most 64 characters (SPEC §7.1).</summary>
public static class TrackIds
{
    public const int MaxLength = 64;

    public static string Create(TmGame game, long tmxId, string mapName)
    {
        var prefix = $"{game.Id()}_{tmxId.ToString(CultureInfo.InvariantCulture)}";
        var slug = TmText.Slugify(TmText.StripFormatting(mapName), MaxLength - prefix.Length - 1);
        return slug.Length == 0 ? prefix : $"{prefix}_{slug}";
    }
}
