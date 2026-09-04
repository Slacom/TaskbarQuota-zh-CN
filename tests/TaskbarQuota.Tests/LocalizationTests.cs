using TaskbarQuota.Localization;
using System.Text.RegularExpressions;

namespace TaskbarQuota.Tests;

public sealed class LocalizationTests
{
    private static readonly string[] RequiredScreenCopy =
    [
        "Refresh",
        "Pinned",
        "Pin",
        "Widget",
        "Ignored",
        "Usage dashboard",
        "Usage",
        "Balance",
        "Additional usage",
        "Spend",
        "Monthly",
        "Model",
        "Day",
        "Today",
        "Yesterday",
        "Last 30 Days",
        "Usage trend",
        "Settings",
        "Appearance",
        "Theme",
        "Use system setting",
        "Light",
        "Dark",
        "Widget layout",
        "Bars only",
        "Percentages only",
        "Bars and percentages",
        "Percentage display",
        "Consumed",
        "Remaining",
        "Where to show usage",
        "In the taskbar",
        "Floating always-on-top window",
        "Providers",
        "Behavior",
        "Open at startup",
        "Notifications",
        "Quota alerts",
        "Credentials",
        "About",
        "Refresh usage",
        "Install update",
        "No daily data",
        "No local usage history found",
        "Agent activity",
        "Monitoring",
        "Activity Widget",
        "No recent agent activity",
        "Cost",
        "Open TaskbarQuota",
        "Move usage widget",
        "Reset widget positions",
        "Quit",
    ];

    [Theory]
    [InlineData("Session", "会话")]
    [InlineData("Weekly", "每周")]
    [InlineData("Credits", "额度")]
    [InlineData("Reset credits", "重置机会")]
    [InlineData("Usage history", "使用记录")]
    public void TranslateLabel_KnownUiLabel_ReturnsSimplifiedChinese(string source, string expected)
    {
        Assert.Equal(expected, UiText.TranslateLabel(source));
    }

    [Fact]
    public void TranslateLabel_ProviderOrModelName_PreservesOriginalText()
    {
        Assert.Equal("GPT-5.6 Sol", UiText.TranslateLabel("GPT-5.6 Sol"));
    }

    [Theory]
    [InlineData("4h 49m", "4小时49分")]
    [InlineData("6d 23h", "6天23小时")]
    [InlineData("29d17h", "29天17小时")]
    [InlineData("now", "现在")]
    public void FormatDuration_EnglishAbbreviations_ReturnsChineseUnits(string source, string expected)
    {
        Assert.Equal(expected, UiText.FormatDuration(source));
    }

    [Fact]
    public void FormatResetDescription_Countdown_ReturnsChineseSentence()
    {
        Assert.Equal("4小时49分后重置", UiText.FormatResetDescription("4h 49m"));
    }

    [Theory]
    [InlineData(1, "1 次可用")]
    [InlineData(3, "3 次可用")]
    public void FormatAvailability_UsesChineseCounter(int count, string expected)
    {
        Assert.Equal(expected, UiText.FormatAvailability(count));
    }

    [Fact]
    public void FormatUpdatedAt_UsesChineseStatusText()
    {
        Assert.Equal("更新于 15:24:30", UiText.FormatUpdatedAt(new DateTime(2026, 9, 4, 15, 24, 30)));
    }

    [Fact]
    public void FormatResetAt_UsesUnambiguousChineseDateTime()
    {
        var value = new DateTimeOffset(2026, 10, 4, 9, 6, 0, TimeSpan.FromHours(8));

        Assert.Equal("2026年10月4日 09:06 重置", UiText.FormatResetAt(value));
    }

    [Theory]
    [InlineData(1_450, "1.45K Token")]
    [InlineData(1_450_000, "1.45M Token")]
    [InlineData(1_450_000_000, "1.45B Token")]
    public void FormatTokens_UsesCompactValueAndChineseUiUnit(long tokens, string expected)
    {
        Assert.Equal(expected, UiText.FormatTokens(tokens));
    }

    [Fact]
    public void FormatDetectedVia_UsesChineseConnector()
    {
        Assert.Equal("通过 Codex CLI 检测", UiText.FormatDetectedVia("Codex CLI"));
        Assert.Equal("经 Codex CLI", UiText.FormatShortVia("Codex CLI"));
    }

    [Fact]
    public void Get_UnknownKey_ReturnsKeyInsteadOfBlankText()
    {
        Assert.Equal("UntranslatedKey", UiText.Get("UntranslatedKey"));
    }

    [Fact]
    public void RequiredScreenCopy_HasSimplifiedChineseTranslation()
    {
        foreach (var key in RequiredScreenCopy)
        {
            var translated = UiText.Get(key);
            Assert.NotEqual(key, translated);
            Assert.Contains(translated, static character => character is >= '\u4e00' and <= '\u9fff');
        }
    }

    [Fact]
    public void XamlSurfaces_DoNotKeepTranslatedEnglishLiterals()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "TaskbarQuota.App");
        var xaml = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        foreach (var key in RequiredScreenCopy)
        {
            var pattern = new Regex(
                $@"(?:Text|Content|Header|Description|Title|PlaceholderText|AutomationProperties\.Name|ToolTipService\.ToolTip)\s*=\s*\""{Regex.Escape(key)}\""",
                RegexOptions.CultureInvariant);
            Assert.DoesNotMatch(pattern, xaml);
        }

        Assert.Contains("using:TaskbarQuota.Localization", xaml, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TaskbarQuota.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the TaskbarQuota repository root.");
    }
}
