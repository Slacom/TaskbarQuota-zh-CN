using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaskbarQuota.Localization;
using TaskbarQuota.Usage;

namespace TaskbarQuota.ViewModels
{
    public sealed class ProviderUsageHistoryViewModel
    {
        public static ProviderUsageHistoryViewModel Empty { get; } = Create(null, DateTime.Today);

        public bool HasData { get; }
        public bool HasTrend { get; }
        public string SourceNote { get; }
        public string TrendAutomationName { get; }
        public IReadOnlyList<ProviderUsagePeriodRowViewModel> Periods { get; }
        public IReadOnlyList<ProviderUsageTrendPointViewModel> TrendPoints { get; }

        private ProviderUsageHistoryViewModel(
            bool hasData,
            string sourceNote,
            IReadOnlyList<ProviderUsagePeriodRowViewModel> periods,
            IReadOnlyList<ProviderUsageTrendPointViewModel> trendPoints)
        {
            HasData = hasData;
            SourceNote = sourceNote;
            Periods = periods;
            TrendPoints = trendPoints;
            HasTrend = trendPoints.Any(point => point.Tokens > 0);

            var peak = trendPoints.OrderByDescending(point => point.Tokens).FirstOrDefault();
            TrendAutomationName = peak is null || peak.Tokens == 0
                ? "最近 30 天使用趋势。暂无每日 Token 数据。"
                : $"最近 30 天使用趋势。峰值为 {peak.Date:M月d日} 的 {FormatTokens(peak.Tokens)}。";
        }

        public static ProviderUsageHistoryViewModel From(UsageHistory? history)
            => Create(history, DateTime.Today);

        internal static ProviderUsageHistoryViewModel CreateForTesting(UsageHistory? history, DateTime today)
            => Create(history, today.Date);

        private static ProviderUsageHistoryViewModel Create(UsageHistory? history, DateTime today)
        {
            if (history is null)
                return new ProviderUsageHistoryViewModel(false, string.Empty, Array.Empty<ProviderUsagePeriodRowViewModel>(), Array.Empty<ProviderUsageTrendPointViewModel>());

            var periods = new[]
            {
                new ProviderUsagePeriodRowViewModel(UiText.Get("Today"), history.Today),
                new ProviderUsagePeriodRowViewModel(UiText.Get("Yesterday"), history.Yesterday),
                new ProviderUsagePeriodRowViewModel(UiText.Get("Last 30 Days"), history.Last30Days),
            };
            var byDate = history.Daily
                .Select(usage => (Usage: usage, Parsed: ParseDate(usage.Date)))
                .Where(item => item.Parsed.HasValue)
                .GroupBy(item => item.Parsed!.Value.Date)
                .ToDictionary(group => group.Key, group => group.Last().Usage);
            var trend = new List<ProviderUsageTrendPointViewModel>(30);
            for (int offset = 29; offset >= 0; offset--)
            {
                var day = today.AddDays(-offset);
                byDate.TryGetValue(day, out var usage);
                trend.Add(new ProviderUsageTrendPointViewModel(day, usage));
            }

            var sourceNote = history.Today?.ModelBreakdown?.SourceNote
                ?? history.Last30Days?.ModelBreakdown?.SourceNote
                ?? history.Yesterday?.ModelBreakdown?.SourceNote
                ?? string.Empty;
            bool hasData = periods.Any(period => period.HasData) || trend.Any(point => point.Tokens > 0);
            return new ProviderUsageHistoryViewModel(hasData, sourceNote, periods, trend);
        }

        private static DateTime? ParseDate(string value)
            => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed.Date
                : null;

        internal static string FormatTokens(ulong tokens)
        {
            return UiText.FormatTokens(tokens);
        }
    }

    public sealed class ProviderUsagePeriodRowViewModel
    {
        public string Label { get; }
        public bool HasData { get; }
        public string Reading { get; }
        public string TooltipText { get; }

        public ProviderUsagePeriodRowViewModel(string label, UsagePeriod? period)
        {
            Label = label;
            HasData = period is not null;
            if (period is null)
            {
                Reading = "无数据";
                TooltipText = $"{label}：无本地使用数据";
                return;
            }

            var tokens = ProviderUsageHistoryViewModel.FormatTokens(period.Tokens);
            Reading = period.EstimatedCostUsd is { } cost
                ? $"${cost:N2} · {tokens}"
                : tokens;
            var figures = period.EstimatedCostUsd is { } exactCost
                ? $"${exactCost:N2} · {period.Tokens:N0} Token"
                : $"{period.Tokens:N0} Token · 成本不可用";
            TooltipText = period.CostEstimated && period.EstimatedCostUsd.HasValue
                ? $"{figures}\nAPI 等值估算；可能与订阅计费不同"
                : figures;
        }
    }

    public sealed class ProviderUsageTrendPointViewModel
    {
        public DateTime Date { get; }
        public ulong Tokens { get; }
        public double? CostUsd { get; }
        public string TooltipText { get; }

        public ProviderUsageTrendPointViewModel(DateTime date, DailyUsage? usage)
        {
            Date = date.Date;
            Tokens = usage?.Tokens ?? 0;
            CostUsd = usage?.EstimatedCostUsd;
            TooltipText = CostUsd is { } cost
                ? $"{Date:M月d日 dddd}：{Tokens:N0} Token · ${cost:N2}"
                : $"{Date:M月d日 dddd}：{Tokens:N0} Token";
        }
    }
}
