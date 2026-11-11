using System.Text.Json;
using TaskbarQuota;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

[Collection(WidgetRowSettingsCollection.Name)]
public sealed class UsageDiscoveryRecordingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TaskbarQuota-discovery-" + Guid.NewGuid().ToString("N"));
    private readonly IDisposable _storageOverride;
    private readonly Func<ProviderId, bool>? _installedOverride;
    private readonly bool _autoHide;
    private readonly Dictionary<ProviderId, (bool? Widget, bool? Dashboard, bool Pinned)> _settings;

    public UsageDiscoveryRecordingTests()
    {
        _installedOverride = ProviderInstallDetector.IsInstalledOverrideForTesting;
        _autoHide = WidgetSettingsService.AutoHideUnavailable;
        _settings = Enum.GetValues<ProviderId>().ToDictionary(id => id, id => (
            Widget: WidgetSettingsService.TryGetProviderVisibilityOverride(id, out bool widget) ? (bool?)widget : null,
            Dashboard: WidgetSettingsService.TryGetDashboardProviderVisibilityOverride(id, out bool dashboard) ? (bool?)dashboard : null,
            Pinned: WidgetSettingsService.IsProviderPinned(id)));
        Directory.CreateDirectory(_directory);
        _storageOverride = AppStorage.OverrideAppDataDirectoryForTesting(_directory);
        ProviderDiscoveryService.ResetForTesting();
        ProviderInstallDetector.IsInstalledOverrideForTesting = _ => false;
        WidgetSettingsService.ResetProviderVisibilityForTesting();
        WidgetSettingsService.ResetDashboardProviderVisibilityForTesting();
        WidgetSettingsService.ResetProviderPinsForTesting();
        WidgetSettingsService.ApplyAutoHideUnavailable(true);
    }

    [Fact]
    public async Task SuccessfulFetchRecordsDiscoveryAndKeepsAnUndetectedClaudePinAfterFocusChanges()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        var service = CreateService(provider, ProviderDiscoveryService.RecordFetchResult);
        WidgetSettingsService.SetProviderPinnedForTesting(ProviderId.Claude, true);

        var result = await service.FetchAsync(ProviderId.Claude, force: true);

        Assert.True(result.Ok);
        Assert.True(ProviderDiscoveryService.IsProbed(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.IsConfigured(ProviderId.Claude));
        var displayed = UsageCoordinator.ComputeWidgetDisplayProviders(
            ProviderId.Codex, true, new[] { ProviderId.Codex, ProviderId.Claude },
            Enum.GetValues<ProviderId>(), WidgetSettingsService.IsProviderPinned,
            WidgetSettingsService.IsProviderVisible,
            id => ProviderInstallDetector.IsInstalled(id) || ProviderDiscoveryService.IsConfigured(id));
        Assert.Equal(new[] { ProviderId.Claude, ProviderId.Codex }, displayed);

        using var state = JsonDocument.Parse(File.ReadAllText(StatePath));
        Assert.Contains("Claude", state.RootElement.GetProperty("Probed").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("Claude", state.RootElement.GetProperty("Configured").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task CacheHitIsNotObservedButAnUnchangedLiveSuccessIsObserved()
    {
        // Copilot has no local history reader, so exercising the cache path cannot inspect user logs.
        var provider = new FakeProvider(ProviderId.Copilot);
        var observed = new List<UsageResult>();
        var service = CreateService(provider, observed.Add);

        var first = await service.FetchAsync(provider.Id, force: true);
        await service.FetchAsync(provider.Id);
        Assert.Single(observed);
        Assert.Equal(1, provider.FetchCount);

        var unchanged = await service.FetchAsync(provider.Id, force: true);
        Assert.Equal(2, observed.Count);
        Assert.All(observed, result => Assert.True(result.Ok));
        Assert.Equal(2, provider.FetchCount);
        Assert.Same(first.Fetch, unchanged.Fetch);
    }

    [Fact]
    public async Task RepeatedSuccessDoesNotRewriteDiscoveryOrRepeatChangeNotification()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        var service = CreateService(provider, ProviderDiscoveryService.RecordFetchResult);
        var changed = new List<ProviderId>();
        Action<ProviderId> handler = changed.Add;
        ProviderDiscoveryService.Changed += handler;
        try
        {
            await service.FetchAsync(provider.Id, force: true);
            var content = File.ReadAllText(StatePath);
            File.SetLastWriteTimeUtc(StatePath, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var previousWrite = File.GetLastWriteTimeUtc(StatePath);

            await service.FetchAsync(provider.Id, force: true);

            Assert.Equal(previousWrite, File.GetLastWriteTimeUtc(StatePath));
            Assert.Equal(content, File.ReadAllText(StatePath));
            Assert.Equal(new[] { provider.Id }, changed);
        }
        finally
        {
            ProviderDiscoveryService.Changed -= handler;
        }
    }

    [Fact]
    public async Task FailedPersistenceIsRetriedEvenWhenTheNextSuccessIsUnchanged()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        var service = CreateService(provider, ProviderDiscoveryService.RecordFetchResult);
        // A directory at the file destination makes the first write fail deterministically.
        Directory.CreateDirectory(StatePath);

        await service.FetchAsync(provider.Id, force: true);
        Assert.True(ProviderDiscoveryService.IsConfigured(provider.Id));
        Directory.Delete(StatePath);

        await service.FetchAsync(provider.Id, force: true);

        using var state = JsonDocument.Parse(File.ReadAllText(StatePath));
        Assert.Contains("Claude", state.RootElement.GetProperty("Configured").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task RateLimitedAttemptIsObservedAsFailureEvenWhenPreviousUsageIsReturned()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        var observed = new List<UsageResult>();
        var service = CreateService(provider, observed.Add);
        var first = await service.FetchAsync(provider.Id, force: true);
        provider.NextException = new ProviderException(ProviderErrorKind.RateLimited, "Synthetic rate limit");

        var fallback = await service.FetchAsync(provider.Id, force: true);

        Assert.True(fallback.Ok);
        Assert.Same(first.Fetch, fallback.Fetch);
        Assert.Equal(UsageObservationOrigin.FailureFallback, fallback.ObservationOrigin);
        Assert.Equal(2, observed.Count);
        Assert.False(observed[1].Ok);
        Assert.Equal(ProviderErrorKind.RateLimited, observed[1].ErrorKind);
    }

    [Fact]
    public async Task FailedProbeThenSuccessfulSetupRaisesBothStateTransitions()
    {
        var provider = new FakeProvider(ProviderId.Copilot)
        {
            NextException = new InvalidOperationException("Synthetic provider failure"),
        };
        var service = CreateService(provider, ProviderDiscoveryService.RecordFetchResult);
        var changed = new List<ProviderId>();
        Action<ProviderId> handler = changed.Add;
        ProviderDiscoveryService.Changed += handler;
        try
        {
            var failure = await service.FetchAsync(provider.Id, force: true);
            Assert.False(failure.Ok);
            Assert.True(ProviderDiscoveryService.IsProbed(provider.Id));
            Assert.False(ProviderDiscoveryService.IsConfigured(provider.Id));
            Assert.Single(changed);

            provider.NextException = null;
            await service.FetchAsync(provider.Id, force: true);
            Assert.True(ProviderDiscoveryService.IsConfigured(provider.Id));
            Assert.Equal(new[] { provider.Id, provider.Id }, changed);
        }
        finally
        {
            ProviderDiscoveryService.Changed -= handler;
        }
    }

    [Theory]
    [InlineData(ProviderErrorKind.AuthRequired, true)]
    [InlineData(ProviderErrorKind.NotInstalled, false)]
    public async Task FailedFetchRecordsItsStateWithoutRemovingAnExplicitPin(ProviderErrorKind kind, bool configured)
    {
        var provider = new FakeProvider(ProviderId.Claude)
        {
            NextException = new ProviderException(kind, "Synthetic setup failure"),
        };
        var service = CreateService(provider, ProviderDiscoveryService.RecordFetchResult);
        WidgetSettingsService.SetProviderPinnedForTesting(provider.Id, true);

        var result = await service.FetchAsync(provider.Id, force: true);

        Assert.False(result.Ok);
        Assert.True(ProviderDiscoveryService.IsProbed(provider.Id));
        Assert.Equal(configured, ProviderDiscoveryService.IsConfigured(provider.Id));
        Assert.True(WidgetSettingsService.IsProviderPinned(provider.Id));
        Assert.True(WidgetSettingsService.IsProviderVisible(provider.Id));
        Assert.True(WidgetSettingsService.IsProviderDashboardVisible(provider.Id));
    }

    [Fact]
    public async Task NotInstalledDoesNotHideAnExplicitlyEnabledProvider()
    {
        var provider = new FakeProvider(ProviderId.Claude)
        {
            NextException = new ProviderException(ProviderErrorKind.NotInstalled, "Synthetic missing installation"),
        };
        var service = CreateService(provider, ProviderDiscoveryService.RecordFetchResult);
        ProviderDiscoveryService.EnableProvider(provider.Id);

        await service.FetchAsync(provider.Id, force: true);

        Assert.True(WidgetSettingsService.IsProviderVisible(provider.Id));
        Assert.True(WidgetSettingsService.IsProviderDashboardVisible(provider.Id));
        Assert.True(ProviderDiscoveryService.IsExplicitlyEnabled(provider.Id));
    }

    [Fact]
    public async Task SuccessfulFetchDoesNotUndoExplicitDisableOrWidgetHide()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        var service = CreateService(provider, ProviderDiscoveryService.RecordFetchResult);
        ProviderDiscoveryService.MarkExplicitlyDisabledForTesting(provider.Id);
        ProviderDiscoveryService.MarkExplicitlyWidgetDisabledForTesting(provider.Id);
        WidgetSettingsService.SetProviderVisibleForTesting(provider.Id, false);
        WidgetSettingsService.SetProviderDashboardVisibleForTesting(provider.Id, false);

        await service.FetchAsync(provider.Id, force: true);

        Assert.True(ProviderDiscoveryService.IsConfigured(provider.Id));
        Assert.True(ProviderDiscoveryService.IsExplicitlyDisabled(provider.Id));
        Assert.False(WidgetSettingsService.IsProviderVisible(provider.Id));
        Assert.False(WidgetSettingsService.IsProviderDashboardVisible(provider.Id));
        Assert.False(ProviderDiscoveryService.ShouldFetch(provider.Id, active: provider.Id));
    }

    [Fact]
    public async Task CancelledAttemptIsNotObservedOrRecorded()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        var observed = new List<UsageResult>();
        var service = CreateService(provider, result =>
        {
            observed.Add(result);
            ProviderDiscoveryService.RecordFetchResult(result);
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FetchAsync(provider.Id, true, cancellation.Token));

        Assert.Empty(observed);
        Assert.False(ProviderDiscoveryService.IsProbed(provider.Id));
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async Task ThrowingObserverDoesNotChangeSuccessfulFetchOrRunTwice()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        int calls = 0;
        var service = CreateService(provider, _ =>
        {
            calls++;
            throw new InvalidOperationException("Synthetic observer failure");
        });

        var result = await service.FetchAsync(provider.Id, force: true);

        Assert.True(result.Ok);
        Assert.Equal(42, result.Fetch!.Usage.Primary.UsedPercent);
        Assert.Equal(1, calls);
        Assert.Equal(1, provider.FetchCount);
    }

    [Fact]
    public void RestoringSnapshotDoesNotCountAsAProviderAttempt()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        var result = UsageResult.Success(provider.Id, provider, provider.Success());
        UsageSnapshotStore.Save(_directory, new Dictionary<ProviderId, UsageResult> { [provider.Id] = result });
        var observed = new List<UsageResult>();

        var service = new UsageService(_directory, observed.Add);

        Assert.True(service.TryGetLastSuccessfulLiveResult(provider.Id, out var restored));
        Assert.True(restored.IsStale);
        Assert.Empty(observed);
        Assert.False(ProviderDiscoveryService.IsProbed(provider.Id));
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async Task ChangeNotificationRunsAfterDiscoveryLockIsReleased()
    {
        var provider = new FakeProvider(ProviderId.Claude);
        var service = CreateService(provider, ProviderDiscoveryService.RecordFetchResult);
        bool notificationReceived = false;
        bool readerCompleted = false;
        Action<ProviderId> handler = id =>
        {
            notificationReceived = true;
            // A different thread must be able to acquire the discovery lock before this handler returns.
            var reader = Task.Run(() => ProviderDiscoveryService.IsConfigured(id));
            readerCompleted = reader.Wait(TimeSpan.FromSeconds(2)) && reader.Result;
        };
        ProviderDiscoveryService.Changed += handler;
        try
        {
            await service.FetchAsync(provider.Id, force: true);
            Assert.True(notificationReceived);
            Assert.True(readerCompleted);
        }
        finally
        {
            ProviderDiscoveryService.Changed -= handler;
        }
    }

    private string StatePath => Path.Combine(_directory, "provider-discovery.json");

    private static UsageService CreateService(FakeProvider provider, Action<UsageResult> observer)
    {
        var service = new UsageService(fetchObserver: observer);
        service.Register(provider);
        return service;
    }

    public void Dispose()
    {
        ProviderDiscoveryService.ResetForTesting();
        ProviderInstallDetector.IsInstalledOverrideForTesting = _installedOverride;
        WidgetSettingsService.ResetProviderVisibilityForTesting();
        WidgetSettingsService.ResetDashboardProviderVisibilityForTesting();
        WidgetSettingsService.ResetProviderPinsForTesting();
        foreach (var (id, saved) in _settings)
        {
            if (saved.Widget is bool widget)
                WidgetSettingsService.SetProviderVisibleForTesting(id, widget);
            if (saved.Dashboard is bool dashboard)
                WidgetSettingsService.SetProviderDashboardVisibleForTesting(id, dashboard);
            if (saved.Pinned)
                WidgetSettingsService.SetProviderPinnedForTesting(id, true);
        }
        WidgetSettingsService.ApplyAutoHideUnavailable(_autoHide);
        _storageOverride.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeProvider(ProviderId id) : IUsageProvider
    {
        private readonly DateTimeOffset _resetAt = DateTimeOffset.Now.AddHours(4);
        public ProviderId Id => id;
        public string DisplayName => "Synthetic provider";
        public string SessionLabel => "Session";
        public string WeeklyLabel => "Weekly";
        public BillingKind Billing => BillingKind.Subscription;
        public Exception? NextException { get; set; }
        public int FetchCount { get; private set; }

        public Task<ProviderFetchResult> FetchUsageAsync(CancellationToken ct = default)
        {
            FetchCount++;
            ct.ThrowIfCancellationRequested();
            return NextException is { } error
                ? Task.FromException<ProviderFetchResult>(error)
                : Task.FromResult(Success());
        }

        public ProviderFetchResult Success() => new(new UsageSnapshot(new RateWindow(42, 300, _resetAt))
        {
            Secondary = new RateWindow(73, 10080, _resetAt),
            UsageHistory = new UsageHistory(),
        }, "Synthetic test response");
    }
}
