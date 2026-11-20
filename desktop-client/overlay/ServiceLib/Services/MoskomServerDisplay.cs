using System.Text;
using System.Text.RegularExpressions;

namespace ServiceLib.Services;

public static class MoskomServerDisplay
{
    private static readonly Regex CountryPrefix = new(
        "^(?<protocol>\\[[^\\]]+\\])\\s+(?<country>[A-Za-z]{2})(?=\\s|$)\\s*(?<name>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> CountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ad", "ae", "af", "ag", "ai", "al", "am", "ao", "aq", "ar", "as", "at", "au", "aw", "ax", "az",
        "ba", "bb", "bd", "be", "bf", "bg", "bh", "bi", "bj", "bl", "bm", "bn", "bo", "bq", "br", "bs", "bt", "bv", "bw", "by", "bz",
        "ca", "cc", "cd", "cf", "cg", "ch", "ci", "ck", "cl", "cm", "cn", "co", "cr", "cu", "cv", "cw", "cx", "cy", "cz",
        "de", "dj", "dk", "dm", "do", "dz", "ec", "ee", "eg", "eh", "er", "es", "et", "fi", "fj", "fk", "fm", "fo", "fr",
        "ga", "gb", "gd", "ge", "gf", "gg", "gh", "gi", "gl", "gm", "gn", "gp", "gq", "gr", "gs", "gt", "gu", "gw", "gy",
        "hk", "hm", "hn", "hr", "ht", "hu", "id", "ie", "il", "im", "in", "io", "iq", "ir", "is", "it", "je", "jm", "jo", "jp",
        "ke", "kg", "kh", "ki", "km", "kn", "kp", "kr", "kw", "ky", "kz", "la", "lb", "lc", "li", "lk", "lr", "ls", "lt", "lu", "lv", "ly",
        "ma", "mc", "md", "me", "mf", "mg", "mh", "mk", "ml", "mm", "mn", "mo", "mp", "mq", "mr", "ms", "mt", "mu", "mv", "mw", "mx", "my", "mz",
        "na", "nc", "ne", "nf", "ng", "ni", "nl", "no", "np", "nr", "nu", "nz", "om", "pa", "pe", "pf", "pg", "ph", "pk", "pl", "pm", "pn", "pr", "ps", "pt", "pw", "py",
        "qa", "re", "ro", "rs", "ru", "rw", "sa", "sb", "sc", "sd", "se", "sg", "sh", "si", "sj", "sk", "sl", "sm", "sn", "so", "sr", "ss", "st", "sv", "sx", "sy", "sz",
        "tc", "td", "tf", "tg", "th", "tj", "tk", "tl", "tm", "tn", "to", "tr", "tt", "tv", "tw", "tz", "ua", "ug", "um", "us", "uy", "uz",
        "va", "vc", "ve", "vg", "vi", "vn", "vu", "wf", "ws", "ye", "yt", "za", "zm", "zw"
    };

    public static string WithCountryFlag(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return summary ?? string.Empty;
        var match = CountryPrefix.Match(summary);
        if (!match.Success) return summary;
        var country = match.Groups["country"].Value.ToLowerInvariant();
        if (!CountryCodes.Contains(country)) return summary;
        var flag = new StringBuilder(2);
        foreach (var letter in country) flag.Append(char.ConvertFromUtf32(0x1F1E6 + letter - 'a'));
        return $"{match.Groups["protocol"].Value} {flag} {match.Groups["name"].Value}".TrimEnd();
    }

    public static bool TrySplit(string? summary, out string protocol, out string country, out string name)
    {
        protocol = country = name = string.Empty;
        if (string.IsNullOrWhiteSpace(summary)) return false;
        var match = CountryPrefix.Match(summary);
        if (!match.Success) return false;
        country = match.Groups["country"].Value.ToLowerInvariant();
        if (!CountryCodes.Contains(country)) return false;
        protocol = match.Groups["protocol"].Value;
        name = match.Groups["name"].Value;
        return true;
    }
}
