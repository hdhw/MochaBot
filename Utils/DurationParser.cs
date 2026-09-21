using System.Text.RegularExpressions;

namespace MochaBot.Utils;

public static partial class DurationParser
{
    [GeneratedRegex(@"(\d+)\s*(d|h|m|s|w)", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    public static TimeSpan? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var matches = TokenRegex().Matches(input);
        if (matches.Count == 0)
            return null;

        var total = TimeSpan.Zero;
        foreach (Match match in matches)
        {
            var value = int.Parse(match.Groups[1].Value);
            total += match.Groups[2].Value.ToLowerInvariant() switch
            {
                "w" => TimeSpan.FromDays(value * 7),
                "d" => TimeSpan.FromDays(value),
                "h" => TimeSpan.FromHours(value),
                "m" => TimeSpan.FromMinutes(value),
                "s" => TimeSpan.FromSeconds(value),
                _ => TimeSpan.Zero,
            };
        }

        return total > TimeSpan.Zero ? total : null;
    }

    public static string Humanize(TimeSpan span)
    {
        if (span.TotalSeconds < 1)
            return "0s";

        var parts = new List<string>();
        if (span.Days > 0) parts.Add($"{span.Days}d");
        if (span.Hours > 0) parts.Add($"{span.Hours}h");
        if (span.Minutes > 0) parts.Add($"{span.Minutes}m");
        if (span.Seconds > 0 && span.TotalHours < 1) parts.Add($"{span.Seconds}s");
        return string.Join(" ", parts);
    }
}
