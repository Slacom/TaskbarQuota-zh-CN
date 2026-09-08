using System;
using System.Threading;
using System.Threading.Tasks;
using TaskbarQuota.Controls;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

[Collection(WidgetRowSettingsCollection.Name)]
public class CodexOfflineWidgetTests
{
    [Fact]
    public void FailureFallback_RendersNeutralChineseQuotaRows()
    {
        var fallback = LiveCodexResult(12, 34).AsFailureFallback(2, DateTimeOffset.UtcNow);

        var rows = WidgetSummary.BuildCodexUnavailableRowsForTesting(fallback);

        Assert.Equal(
            new[]
            {
                (Label: "5小时额度", Value: "--", HasBar: false),
                (Label: "每周额度", Value: "--", HasBar: false),
            },
            rows);
    }

    [Fact]
    public void LocalHistoryFallback_RendersNeutralChineseQuotaRows()
    {
        var localHistoryFallback = LiveCodexResult(12, 34)
            .AsLocalHistoryFallback(2, DateTimeOffset.UtcNow);

        Assert.True(WidgetSummary.ShouldRenderNeutralCodexQuotaForTesting(localHistoryFallback));
        Assert.Equal(
            new[]
            {
                (Label: "5小时额度", Value: "--", HasBar: false),
                (Label: "每周额度", Value: "--", HasBar: false),
            },
            WidgetSummary.BuildCodexUnavailableRowsForTesting(localHistoryFallback));
    }

    [Fact]
    public void LocalHistoryFallback_RemainsUnconfirmedWhenReadFromCache()
    {
        var localHistoryFallback = LiveCodexResult(12, 34)
            .AsLocalHistoryFallback(2, DateTimeOffset.UtcNow);

        Assert.Equal(
            UsageObservationOrigin.LocalHistoryFallback,
            localHistoryFallback.AsMemoryCache().ObservationOrigin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ProviderErrorKind.Timeout)]
    [InlineData(ProviderErrorKind.RateLimited)]
    [InlineData(ProviderErrorKind.Other)]
    public void CodexNetworkFailure_UsesNeutralQuotaRows(ProviderErrorKind? kind)
    {
        var failure = UsageResult.Failure(
            ProviderId.Codex,
            "network unavailable",
            LiveCodexResult(12, 34).Provider,
            kind);

        Assert.True(WidgetSummary.ShouldRenderNeutralCodexQuotaForTesting(failure));
    }

    [Fact]
    public void CodexAuthFailure_IsNotMisclassifiedAsNetworkFailure()
    {
        var failure = UsageResult.Failure(
            ProviderId.Codex,
            "login required",
            LiveCodexResult(12, 34).Provider,
            ProviderErrorKind.AuthRequired);

        Assert.False(WidgetSummary.ShouldRenderNeutralCodexQuotaForTesting(failure));
    }

    [Fact]
    public void FallbackAndLiveSnapshotHaveDifferentRenderSignatures()
    {
        var live = LiveCodexResult(12, 34).AsLiveObservation(1, DateTimeOffset.UtcNow);
        var fallback = live.AsFailureFallback(2, DateTimeOffset.UtcNow);

        Assert.NotEqual(
            WidgetSummary.BuildRenderSignatureForTesting(live),
            WidgetSummary.BuildRenderSignatureForTesting(fallback));
    }

    private static UsageResult LiveCodexResult(double primary, double secondary)
    {
        var provider = new TestProvider();
        return UsageResult.Success(
            ProviderId.Codex,
            provider,
            new ProviderFetchResult(
                new UsageSnapshot(new RateWindow(primary))
                {
                    Secondary = new RateWindow(secondary),
                },
                "oauth"));
    }

    private sealed class TestProvider : IUsageProvider
    {
        public ProviderId Id => ProviderId.Codex;
        public string DisplayName => "Codex";
        public string SessionLabel => "Session";
        public string WeeklyLabel => "Weekly";
        public BillingKind Billing => BillingKind.Subscription;

        public Task<ProviderFetchResult> FetchUsageAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
