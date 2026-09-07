using System;
using System.Linq;
using TaskbarQuota.Usage;
using TaskbarQuota.ViewModels;
using Xunit;

namespace TaskbarQuota.Tests
{
    public sealed class ProviderUsageHistoryViewModelTests
    {
        [Fact]
        public void BuildsCompactPeriodReadingsAndFillsThirtyDayTrend()
        {
            var history = new UsageHistory
            {
                Today = new UsagePeriod(1_500_000, 1.25, costEstimated: true),
                Yesterday = new UsagePeriod(0, null),
                Last30Days = new UsagePeriod(2_000_000_000, 349.92),
                Daily = new[]
                {
                    new DailyUsage("2026-08-09", 1_500_000, 1.25),
                    new DailyUsage("2026-08-10", 500_000, null),
                },
            };

            var viewModel = ProviderUsageHistoryViewModel.CreateForTesting(
                history,
                new DateTime(2026, 8, 10));

            Assert.True(viewModel.HasData);
            Assert.True(viewModel.HasTrend);
            Assert.Equal(30, viewModel.TrendPoints.Count);
            Assert.Equal(new DateTime(2026, 7, 12), viewModel.TrendPoints[0].Date);
            Assert.Equal(1_500_000UL, viewModel.TrendPoints[^2].Tokens);
            Assert.Equal("$1.25 · 1.5M Token", viewModel.Periods[0].Reading);
            Assert.Equal("0 Token", viewModel.Periods[1].Reading);
            Assert.Equal("$349.92 · 2B Token", viewModel.Periods[2].Reading);
            Assert.Contains("峰值为 8月9日 的 1.5M Token", viewModel.TrendAutomationName, StringComparison.Ordinal);
        }

        [Fact]
        public void MissingHistoryProducesNoProviderDetails()
        {
            var viewModel = ProviderUsageHistoryViewModel.CreateForTesting(null, new DateTime(2026, 8, 10));

            Assert.False(viewModel.HasData);
            Assert.False(viewModel.HasTrend);
            Assert.Empty(viewModel.Periods);
            Assert.Empty(viewModel.TrendPoints);
        }

        [Fact]
        public void TokenOnlyPeriodsNeverInventCost()
        {
            var row = new ProviderUsagePeriodRowViewModel("Today", new UsagePeriod(35_400_000, null, estimateComplete: false));

            Assert.Equal("35.4M Token", row.Reading);
            Assert.DoesNotContain('$', row.Reading);
            Assert.Contains("成本不可用", row.TooltipText, StringComparison.Ordinal);
        }

        [Fact]
        public void ModelCostBreakdownIsHiddenForCodexOnly()
        {
            Assert.False(ProviderCardViewModel.ShouldShowModelBreakdown(ProviderId.Codex, 2));
            Assert.True(ProviderCardViewModel.ShouldShowModelBreakdown(ProviderId.Claude, 2));
            Assert.False(ProviderCardViewModel.ShouldShowModelBreakdown(ProviderId.Claude, 0));
        }
    }
}
