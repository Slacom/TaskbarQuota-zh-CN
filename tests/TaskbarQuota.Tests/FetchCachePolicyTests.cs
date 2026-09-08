using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

public class FetchCachePolicyTests
{
    [Fact]
    public void TtlForFailure_RateLimited_UsesFiveMinuteBackoff()
        => Assert.Equal(UsageService.RateLimitedCacheTtl, FetchCachePolicy.TtlForFailure(ProviderErrorKind.RateLimited));

    [Fact]
    public void TtlForFailure_OtherErrors_UsesShortBackoff()
        => Assert.Equal(UsageService.FailureCacheTtl, FetchCachePolicy.TtlForFailure(ProviderErrorKind.Other));

    [Fact]
    public void TtlForSuccess_UsesSixtySeconds()
        => Assert.Equal(UsageService.SuccessCacheTtl, FetchCachePolicy.TtlForSuccess());

    [Fact]
    public void Snapshot_UsesChinesePendingMessages()
    {
        var service = new UsageService();

        var snapshots = service.Snapshot(ProviderId.Codex);

        Assert.Equal("正在加载当前服务…", snapshots.Single(result => result.Id == ProviderId.Codex).Error);
        Assert.Equal("正在加载…", snapshots.Single(result => result.Id == ProviderId.Claude).Error);
    }

    [Fact]
    public async Task FetchAsync_UnexpectedException_ReturnsStableChineseError()
    {
        var service = new UsageService();
        var provider = new FlakyProvider(ProviderId.Kimi)
        {
            NextUnexpectedException = new InvalidOperationException("English machine detail should stay out of UI"),
        };
        service.Register(provider);

        var result = await service.FetchAsync(ProviderId.Kimi, force: true);

        Assert.False(result.Ok);
        Assert.Equal("获取用量失败，请稍后重试。", result.Error);
    }

    [Fact]
    public async Task FetchAsync_UnexpectedExceptionWithLocalHistory_MarksResultAsUnconfirmed()
    {
        var directory = Directory.CreateTempSubdirectory("taskbarquota-codex-history-");
        string? previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            var sessions = Directory.CreateDirectory(Path.Combine(directory.FullName, "sessions"));
            File.WriteAllText(
                Path.Combine(sessions.FullName, "2026-09-08.jsonl"),
                "{\"timestamp\":\"2026-09-08T10:00:00Z\",\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-5\"}}\n"
                + "{\"timestamp\":\"2026-09-08T10:01:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":1,\"output_tokens\":1,\"total_tokens\":2}}}}\n");
            Environment.SetEnvironmentVariable("CODEX_HOME", directory.FullName);

            var service = new UsageService();
            var provider = new FlakyProvider(ProviderId.Codex)
            {
                NextUnexpectedException = new InvalidOperationException("network unavailable"),
            };
            service.Register(provider);

            var result = await service.FetchAsync(ProviderId.Codex, force: true);

            Assert.True(result.Ok);
            Assert.Equal(UsageObservationOrigin.LocalHistoryFallback, result.ObservationOrigin);
            Assert.Equal("Local usage history", result.Fetch!.SourceLabel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FetchAsync_RateLimitedAfterSuccess_ReturnsLastSuccessfulLiveResult()
    {
        var service = new UsageService();
        var provider = new FlakyProvider();
        service.Register(provider);

        var first = await service.FetchAsync(ProviderId.Claude, force: true);
        provider.NextException = new ProviderException(ProviderErrorKind.RateLimited, "429");

        var second = await service.FetchAsync(ProviderId.Claude, force: true);
        var cachedFallback = await service.FetchAsync(ProviderId.Claude);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Same(first.Fetch, second.Fetch);
        Assert.Equal(UsageObservationOrigin.Live, first.ObservationOrigin);
        Assert.Equal(UsageObservationOrigin.FailureFallback, second.ObservationOrigin);
        Assert.Equal(UsageObservationOrigin.FailureFallback, cachedFallback.ObservationOrigin);
        Assert.Equal(42, second.Fetch!.Usage.Primary.UsedPercent);
        Assert.Equal(73, second.Fetch.Usage.Secondary!.UsedPercent);
    }

    [Fact]
    public async Task FetchAsync_TransientFailureAfterSuccess_DoesNotPublishZeroUsage()
    {
        var service = new UsageService();
        var provider = new FlakyProvider();
        service.Register(provider);

        var first = await service.FetchAsync(ProviderId.Claude, force: true);
        provider.NextException = new ProviderException(ProviderErrorKind.Other, "Claude API returned 500");

        var second = await service.FetchAsync(ProviderId.Claude, force: true);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Equal(42, second.Fetch!.Usage.Primary.UsedPercent);
        Assert.Equal(73, second.Fetch.Usage.Secondary!.UsedPercent);
        Assert.NotEqual(0, second.Fetch.Usage.Primary.UsedPercent);
        Assert.NotEqual(0, second.Fetch.Usage.Secondary.UsedPercent);
    }

    [Fact]
    public async Task FetchAsync_ClaudeZeroSnapshotAfterSuccess_DoesNotPublishZeroUsage()
    {
        var service = new UsageService();
        var provider = new FlakyProvider
        {
            NextResetAt = DateTimeOffset.Now.AddHours(4),
        };
        service.Register(provider);

        var first = await service.FetchAsync(ProviderId.Claude, force: true);
        provider.NextPrimaryPercent = 0;
        provider.NextSecondaryPercent = 0;
        provider.NextResetAt = first.Fetch!.Usage.Primary.ResetAt;

        var second = await service.FetchAsync(ProviderId.Claude, force: true);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Equal(42, second.Fetch!.Usage.Primary.UsedPercent);
        Assert.Equal(73, second.Fetch.Usage.Secondary!.UsedPercent);
        Assert.NotEqual(0, second.Fetch.Usage.Primary.UsedPercent);
        Assert.NotEqual(0, second.Fetch.Usage.Secondary.UsedPercent);
    }

    [Fact]
    public async Task FetchAsync_ClaudeZeroSnapshotWithoutSuccess_IsAllowed()
    {
        var service = new UsageService();
        var provider = new FlakyProvider
        {
            NextPrimaryPercent = 0,
            NextSecondaryPercent = 0,
        };
        service.Register(provider);

        var result = await service.FetchAsync(ProviderId.Claude, force: true);

        Assert.True(result.Ok);
        Assert.Equal(0, result.Fetch!.Usage.Primary.UsedPercent);
        Assert.Equal(0, result.Fetch.Usage.Secondary!.UsedPercent);
    }

    [Fact]
    public async Task FetchAsync_ClaudeZeroSnapshotAfterResetAdvanced_IsAllowed()
    {
        var service = new UsageService();
        var provider = new FlakyProvider
        {
            NextResetAt = DateTimeOffset.Now.AddMinutes(5),
        };
        service.Register(provider);

        var first = await service.FetchAsync(ProviderId.Claude, force: true);
        provider.NextPrimaryPercent = 0;
        provider.NextSecondaryPercent = 0;
        provider.NextResetAt = first.Fetch!.Usage.Primary.ResetAt!.Value.AddHours(5);

        var second = await service.FetchAsync(ProviderId.Claude, force: true);

        Assert.True(second.Ok);
        Assert.Equal(0, second.Fetch!.Usage.Primary.UsedPercent);
        Assert.Equal(0, second.Fetch.Usage.Secondary!.UsedPercent);
    }

    [Fact]
    public async Task FetchAsync_AuthFailureAfterSuccess_ReturnsFailureInsteadOfStaleUsage()
    {
        var service = new UsageService();
        var provider = new FlakyProvider();
        service.Register(provider);

        var first = await service.FetchAsync(ProviderId.Claude, force: true);
        provider.NextException = new ProviderException(ProviderErrorKind.AuthRequired, "Claude OAuth token expired.");

        var second = await service.FetchAsync(ProviderId.Claude, force: true);

        Assert.True(first.Ok);
        Assert.False(second.Ok);
        Assert.Contains("expired", second.Error);
    }

    [Fact]
    public async Task FetchAsync_SameLiveUsage_ReturnsPreviousSuccessfulResult()
    {
        var service = new UsageService();
        var provider = new FlakyProvider();
        service.Register(provider);

        var first = await service.FetchAsync(ProviderId.Claude, force: true);
        var second = await service.FetchAsync(ProviderId.Claude, force: true);

        Assert.Same(first.Fetch, second.Fetch);
        Assert.Equal(UsageObservationOrigin.Live, first.ObservationOrigin);
        Assert.Equal(UsageObservationOrigin.Live, second.ObservationOrigin);
        Assert.True(second.ObservationSequence > first.ObservationSequence);
        Assert.Equal(2, provider.FetchCount);
    }

    [Fact]
    public async Task FetchAsync_CachedSuccessIsMarkedAsMemoryCache()
    {
        var service = new UsageService();
        var provider = new FlakyProvider();
        service.Register(provider);

        var live = await service.FetchAsync(ProviderId.Claude, force: true);
        var cached = await service.FetchAsync(ProviderId.Claude);

        Assert.Equal(UsageObservationOrigin.Live, live.ObservationOrigin);
        Assert.Equal(UsageObservationOrigin.MemoryCache, cached.ObservationOrigin);
        Assert.Equal(live.ObservationSequence, cached.ObservationSequence);
        Assert.Same(live.Fetch, cached.Fetch);
        Assert.Equal(1, provider.FetchCount);
    }

    private sealed class FlakyProvider : IUsageProvider
    {
        public FlakyProvider(ProviderId id = ProviderId.Claude) => Id = id;

        public ProviderException? NextException { get; set; }
        public Exception? NextUnexpectedException { get; set; }
        public double? NextPrimaryPercent { get; set; }
        public double? NextSecondaryPercent { get; set; }
        public DateTimeOffset? NextResetAt { get; set; }
        public int FetchCount { get; private set; }

        public ProviderId Id { get; }
        public string DisplayName => "Claude Code";
        public string SessionLabel => "Session";
        public string WeeklyLabel => "Weekly";
        public BillingKind Billing => BillingKind.Subscription;

        public Task<ProviderFetchResult> FetchUsageAsync(CancellationToken ct = default)
        {
            FetchCount++;
            if (NextException is { } exception)
            {
                NextException = null;
                throw exception;
            }
            if (NextUnexpectedException is { } unexpected)
            {
                NextUnexpectedException = null;
                throw unexpected;
            }

            var usage = new UsageSnapshot(new RateWindow(NextPrimaryPercent ?? 42, resetAt: NextResetAt))
            {
                Secondary = new RateWindow(NextSecondaryPercent ?? 73, resetAt: NextResetAt),
                LoginMethod = "Max",
            };
            NextPrimaryPercent = null;
            NextSecondaryPercent = null;
            NextResetAt = null;
            return Task.FromResult(new ProviderFetchResult(usage, "live"));
        }
    }
}
