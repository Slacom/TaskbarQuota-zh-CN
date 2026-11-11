using TaskbarQuota.Controls;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

public sealed class WidgetLayoutTests
{
    [Fact]
    public void WidgetText_UsesMicrosoftYaHeiFont()
        => Assert.Equal("Microsoft YaHei", WidgetSummary.WidgetFontFamilyName);

    [Fact]
    public void WidgetText_UsesFixedLineHeightForSharedBaseline()
        => Assert.Equal(17, WidgetSummary.WidgetTextLineHeight);

    [Fact]
    public void QuotaBars_UseSmallDownwardVisualOffset()
        => Assert.Equal(1d, WidgetSummary.WidgetBarVerticalOffset);

    [Fact]
    public void BarsAndPercentages_MixedGroup_UsesValueAndResetColumnsAfterBar()
    {
        Assert.Equal(
            (ValueOffset: 2, ResetOffset: 3),
            WidgetSummary.GetRowColumnOffsetsForTesting(
                WidgetDisplayMode.BarsAndPercentages,
                groupHasBar: true,
                rowHasBar: false));
    }

    [Fact]
    public void BarsAndPercentages_NoBarGroup_UsesThreeMeaningfulColumns()
    {
        Assert.Equal(
            (ValueOffset: 1, ResetOffset: 2),
            WidgetSummary.GetRowColumnOffsetsForTesting(
                WidgetDisplayMode.BarsAndPercentages,
                groupHasBar: false,
                rowHasBar: false));
    }

    [Fact]
    public void ResetColumnWidth_DoesNotReserveSpaceWithoutResetText()
        => Assert.Equal(0, WidgetSummary.GetResetColumnWidthForTesting(0));

    [Fact]
    public void ResetColumnWidth_UsesStableMinimumWhenResetTextExists()
        => Assert.Equal(76, WidgetSummary.GetResetColumnWidthForTesting(10));

    [Fact]
    public void ResetColumnWidth_ExpandsForLongResetText()
        => Assert.Equal(102, WidgetSummary.GetResetColumnWidthForTesting(100));

    [Fact]
    public void CreditsRows_PreserveResetMetadataForAbsoluteDisplay()
    {
        var resetAt = new DateTimeOffset(2026, 10, 4, 9, 6, 0, TimeSpan.FromHours(8));
        var usage = new UsageSnapshot(new RateWindow(20, 300, resetAt, "3小时"));

        var metadata = WidgetSummary.GetCreditsResetMetadataForTesting(usage);

        Assert.Equal(resetAt, metadata.ResetAt);
        Assert.Equal(300, metadata.WindowMinutes);
    }

    [Fact]
    public void AntigravityRows_UseIncludedAndPaceSemantics()
    {
        var now = DateTimeOffset.UtcNow;
        var usage = new UsageSnapshot(new RateWindow(10, 10080, now.AddDays(3), "3天") { IsIncluded = false })
        {
            Secondary = new RateWindow(50, 10080, now.AddDays(3.5), "3天"),
        };

        var rows = WidgetSummary.GetAntigravityDisplayForTesting(usage);

        Assert.Collection(
            rows,
            excluded =>
            {
                Assert.False(excluded.HasBar);
                Assert.Equal("未包含", excluded.Value);
                Assert.Null(excluded.Pace);
            },
            included =>
            {
                Assert.True(included.HasBar);
                Assert.NotNull(included.Pace);
            });
    }

    [Fact]
    public void AntigravityRows_PreserveResetMetadataForEveryWindow()
    {
        var primaryReset = new DateTimeOffset(2026, 10, 4, 9, 6, 0, TimeSpan.FromHours(8));
        var modelReset = primaryReset.AddHours(1);
        var secondaryReset = primaryReset.AddDays(1);
        var monthlyReset = primaryReset.AddDays(30);
        var usage = new UsageSnapshot(new RateWindow(20, 10080, primaryReset, "7天"))
        {
            ModelSpecific = new RateWindow(30, 300, modelReset, "3小时"),
            Secondary = new RateWindow(40, 10080, secondaryReset, "7天"),
            Monthly = new RateWindow(50, 43200, monthlyReset, "30天"),
        };

        var rows = WidgetSummary.GetAntigravityResetMetadataForTesting(usage);

        Assert.Collection(
            rows,
            row =>
            {
                Assert.Equal("每周额度", row.Label);
                Assert.Equal(primaryReset, row.ResetAt);
                Assert.Equal(10080, row.ResetWindowMinutes);
            },
            row =>
            {
                Assert.Equal("5小时", row.Label);
                Assert.Equal(modelReset, row.ResetAt);
                Assert.Equal(300, row.ResetWindowMinutes);
            },
            row =>
            {
                Assert.Equal("每周额度", row.Label);
                Assert.Equal(secondaryReset, row.ResetAt);
                Assert.Equal(10080, row.ResetWindowMinutes);
            },
            row =>
            {
                Assert.Equal("5小时", row.Label);
                Assert.Equal(monthlyReset, row.ResetAt);
                Assert.Equal(43200, row.ResetWindowMinutes);
            });
    }
}
