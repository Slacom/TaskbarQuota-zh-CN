using TaskbarQuota.Localization;

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
}
