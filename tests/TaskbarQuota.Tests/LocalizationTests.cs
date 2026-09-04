using TaskbarQuota.Localization;

namespace TaskbarQuota.Tests;

public sealed class LocalizationTests
{
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
}
