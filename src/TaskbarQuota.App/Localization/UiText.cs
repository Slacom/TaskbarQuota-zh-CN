using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TaskbarQuota.Localization;

/// <summary>
/// Simplified Chinese UI copy for the localized Windows build.
/// Provider names, model names, and unknown server-provided labels are preserved.
/// </summary>
internal static partial class UiText
{
    private static readonly IReadOnlyDictionary<string, string> Values =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Session"] = "会话",
            ["Weekly"] = "每周",
            ["Credits"] = "额度",
            ["Reset credits"] = "重置机会",
            ["Usage history"] = "使用记录",
            ["Now"] = "现在",
        };

    public static string Get(string key)
        => Values.TryGetValue(key, out var value) ? value : key;

    public static string TranslateLabel(string? label)
        => string.IsNullOrWhiteSpace(label) ? string.Empty : Get(label);

    public static string FormatDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
            return string.Empty;

        if (string.Equals(duration.Trim(), "now", StringComparison.OrdinalIgnoreCase))
            return Get("Now");

        return DurationTokenRegex().Replace(
                duration,
                static match => match.Groups["value"].Value
                    + (match.Groups["unit"].Value.ToLowerInvariant() switch
                    {
                        "d" => "天",
                        "h" => "小时",
                        "m" => "分",
                        "s" => "秒",
                        _ => match.Value,
                    }))
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    public static string FormatResetDescription(string? duration)
    {
        var formatted = FormatDuration(duration);
        return string.IsNullOrEmpty(formatted)
            ? string.Empty
            : string.Equals(formatted, Get("Now"), StringComparison.Ordinal)
                ? "即将重置"
                : $"{formatted}后重置";
    }

    public static string FormatAvailability(int count)
        => $"{count.ToString("N0", CultureInfo.CurrentCulture)} 次可用";

    [GeneratedRegex(@"(?<value>\d+)\s*(?<unit>[dhms])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationTokenRegex();
}
