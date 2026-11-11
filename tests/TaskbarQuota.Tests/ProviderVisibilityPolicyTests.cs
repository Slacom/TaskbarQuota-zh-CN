using TaskbarQuota;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

[Collection(WidgetRowSettingsCollection.Name)]
public sealed class ProviderVisibilityPolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TaskbarQuota-visibility-" + Guid.NewGuid().ToString("N"));
    private readonly IDisposable _storageOverride;
    private readonly Func<ProviderId, bool>? _installedOverride;
    private readonly bool _autoHide;
    private readonly Dictionary<ProviderId, (bool? Widget, bool? Dashboard, bool Pinned)> _settings;

    public ProviderVisibilityPolicyTests()
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PinnedUndetectedClaudeSurvivesSwitchToCodex(bool autoHide)
    {
        WidgetSettingsService.ApplyAutoHideUnavailable(autoHide);
        WidgetSettingsService.SetProviderPinnedForTesting(ProviderId.Claude, true);
        var result = ClaudeResult();

        Assert.Equal(new[] { ProviderId.Claude }, DisplayProviders(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(result, ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Claude));

        Assert.Equal(new[] { ProviderId.Claude, ProviderId.Codex }, DisplayProviders(ProviderId.Codex));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(result, ProviderId.Codex));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.True(WidgetSettingsService.IsProviderPinned(ProviderId.Claude));
    }

    [Fact]
    public void ExplicitEnableKeepsUndetectedClaudeInDashboardAfterFocusChanges()
    {
        ProviderDiscoveryService.EnableProvider(ProviderId.Claude);

        Assert.True(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        // Enabling a provider does not itself request an always-visible widget tile.
        Assert.False(WidgetSettingsService.IsProviderPinned(ProviderId.Claude));
        Assert.Equal(new[] { ProviderId.Codex }, DisplayProviders(ProviderId.Codex));
    }

    [Fact]
    public void ConfiguredProviderRemainsEligibleWithoutAnInstalledAppOrActiveWindow()
    {
        ProviderDiscoveryService.MarkConfiguredForTesting(ProviderId.Claude);

        Assert.True(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, active: null));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), active: null));
        Assert.Empty(DisplayProviders(active: null));
    }

    [Fact]
    public void ExplicitDisableWinsOverInstallationConfigurationPinsAndSavedVisibility()
    {
        ProviderInstallDetector.IsInstalledOverrideForTesting = _ => true;
        ProviderDiscoveryService.MarkConfiguredForTesting(ProviderId.Claude);
        ProviderDiscoveryService.EnableProvider(ProviderId.Claude);
        WidgetSettingsService.SetProviderPinnedForTesting(ProviderId.Claude, true);
        WidgetSettingsService.SetProviderVisibleForTesting(ProviderId.Claude, true);
        WidgetSettingsService.SetProviderDashboardVisibleForTesting(ProviderId.Claude, true);
        WidgetSettingsService.ApplyAutoHideUnavailable(false);
        ProviderDiscoveryService.MarkExplicitlyDisabledForTesting(ProviderId.Claude);

        Assert.False(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
    }

    [Fact]
    public void WidgetHideSurvivesEligibilityAndStartupWithoutHidingDashboard()
    {
        ProviderDiscoveryService.MarkConfiguredForTesting(ProviderId.Claude);
        ProviderDiscoveryService.MarkExplicitlyWidgetDisabledForTesting(ProviderId.Claude);
        WidgetSettingsService.SetProviderVisibleForTesting(ProviderId.Claude, false);

        ProviderDiscoveryService.SyncInstalledProviderVisibility();

        Assert.True(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
        Assert.False(WidgetSettingsService.IsProviderVisible(ProviderId.Claude));
        Assert.Empty(DisplayProviders(ProviderId.Claude));
    }

    [Fact]
    public void SavedDashboardHideKeepsInactiveCardHiddenWithoutRemovingPinnedWidget()
    {
        WidgetSettingsService.SetProviderPinnedForTesting(ProviderId.Claude, true);
        WidgetSettingsService.SetProviderDashboardVisibleForTesting(ProviderId.Claude, false);

        ProviderDiscoveryService.SyncInstalledProviderVisibility();

        Assert.True(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
        Assert.Contains(ProviderId.Claude, DisplayProviders(ProviderId.Codex));
        Assert.False(WidgetSettingsService.IsProviderDashboardVisible(ProviderId.Claude));
    }

    [Fact]
    public void ConfiguredHiddenProviderIsNotFetchedUntilActiveOrSurfaceIsShown()
    {
        ProviderDiscoveryService.MarkConfiguredForTesting(ProviderId.Claude);
        WidgetSettingsService.SetProviderVisibleForTesting(ProviderId.Claude, false);
        WidgetSettingsService.SetProviderDashboardVisibleForTesting(ProviderId.Claude, false);

        Assert.True(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Claude));

        WidgetSettingsService.SetProviderVisibleForTesting(ProviderId.Claude, true);
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
    }

    [Fact]
    public void MissedStartupDetectionStillAllowsFocusedClaudeToRecover()
    {
        ProviderDiscoveryService.SyncInstalledProviderVisibility();
        Assert.False(WidgetSettingsService.IsProviderVisible(ProviderId.Claude));
        Assert.False(WidgetSettingsService.IsProviderDashboardVisible(ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.IsEligible(ProviderId.Claude));

        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
    }

    [Fact]
    public void ConfiguredProviderRecoversFromAnEarlierAutomaticHideAtStartup()
    {
        // A previous launch missed detection and auto-hid Claude; Claude was later configured while focused.
        ProviderDiscoveryService.SyncInstalledProviderVisibility();
        Assert.False(WidgetSettingsService.IsProviderDashboardVisible(ProviderId.Claude));
        ProviderDiscoveryService.MarkConfiguredForTesting(ProviderId.Claude);

        ProviderDiscoveryService.SyncInstalledProviderVisibility();

        Assert.True(WidgetSettingsService.IsProviderVisible(ProviderId.Claude));
        Assert.True(WidgetSettingsService.IsProviderDashboardVisible(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
    }

    [Fact]
    public void ConfiguredProviderStartupRecoveryKeepsExplicitDisable()
    {
        ProviderDiscoveryService.MarkConfiguredForTesting(ProviderId.Claude);
        ProviderDiscoveryService.MarkExplicitlyDisabledForTesting(ProviderId.Claude);
        WidgetSettingsService.SetProviderVisibleForTesting(ProviderId.Claude, false);
        WidgetSettingsService.SetProviderDashboardVisibleForTesting(ProviderId.Claude, false);

        ProviderDiscoveryService.SyncInstalledProviderVisibility();

        Assert.False(WidgetSettingsService.IsProviderVisible(ProviderId.Claude));
        Assert.False(WidgetSettingsService.IsProviderDashboardVisible(ProviderId.Claude));
    }

    [Theory]
    [InlineData("configured")]
    [InlineData("enabled")]
    [InlineData("pinned")]
    [InlineData("saved-visible")]
    public void StartupKeepsEligibleUndetectedProviderChoices(string evidence)
    {
        switch (evidence)
        {
            case "configured":
                ProviderDiscoveryService.MarkConfiguredForTesting(ProviderId.Claude);
                break;
            case "enabled":
                ProviderDiscoveryService.EnableProvider(ProviderId.Claude);
                break;
            case "pinned":
                WidgetSettingsService.SetProviderPinnedForTesting(ProviderId.Claude, true);
                break;
            case "saved-visible":
                WidgetSettingsService.ApplyAutoHideUnavailable(false);
                WidgetSettingsService.SetProviderDashboardVisibleForTesting(ProviderId.Claude, true);
                break;
        }

        ProviderDiscoveryService.SyncInstalledProviderVisibility();

        Assert.True(WidgetSettingsService.IsProviderVisible(ProviderId.Claude));
        Assert.True(WidgetSettingsService.IsProviderDashboardVisible(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutoHideControlsWhetherSavedVisibleOverrideKeepsUnknownProviderEligible(bool dashboardOverride)
    {
        if (dashboardOverride)
            WidgetSettingsService.SetProviderDashboardVisibleForTesting(ProviderId.Claude, true);
        else
            WidgetSettingsService.SetProviderVisibleForTesting(ProviderId.Claude, true);

        Assert.False(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));

        WidgetSettingsService.ApplyAutoHideUnavailable(false);
        Assert.True(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));

        WidgetSettingsService.ApplyAutoHideUnavailable(true);
        Assert.False(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
    }

    [Fact]
    public void TurningOffAutoHideDoesNotPollEveryUnknownDefaultProvider()
    {
        WidgetSettingsService.ApplyAutoHideUnavailable(false);

        Assert.All(Enum.GetValues<ProviderId>(), id =>
        {
            Assert.True(WidgetSettingsService.IsProviderVisible(id));
            Assert.True(WidgetSettingsService.IsProviderDashboardVisible(id));
            Assert.False(ProviderDiscoveryService.IsEligible(id));
            Assert.False(ProviderDiscoveryService.ShouldFetch(id, active: null));
        });
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), active: null));
        Assert.Empty(DisplayProviders(active: null));
    }

    [Fact]
    public void SavedHiddenOverridesDoNotOptUnknownProviderIntoPolling()
    {
        WidgetSettingsService.ApplyAutoHideUnavailable(false);
        WidgetSettingsService.SetProviderVisibleForTesting(ProviderId.Claude, false);
        WidgetSettingsService.SetProviderDashboardVisibleForTesting(ProviderId.Claude, false);

        Assert.False(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, active: null));
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), active: null));
    }

    [Fact]
    public void ForegroundDetectionStillAllowsUnknownVisibleProviderUntilFocusChanges()
    {
        Assert.False(ProviderDiscoveryService.IsEligible(ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Claude));
        Assert.True(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Claude));
        Assert.False(ProviderDiscoveryService.ShouldFetch(ProviderId.Claude, ProviderId.Codex));
        Assert.False(ProviderDiscoveryService.ShouldShowInDashboard(ClaudeResult(), ProviderId.Codex));
    }

    [Fact]
    public void ForcedRefreshRespectsEligibilityAndExplicitDisable()
    {
        Assert.False(UsageCoordinator.ShouldFetchForDashboard(ProviderId.Claude, force: true, active: ProviderId.Codex));
        Assert.False(UsageCoordinator.ShouldFetchForDashboard(ProviderId.Copilot, force: true, active: ProviderId.Codex));

        ProviderDiscoveryService.EnableProvider(ProviderId.Claude);
        WidgetSettingsService.SetProviderPinnedForTesting(ProviderId.Copilot, true);
        Assert.True(UsageCoordinator.ShouldFetchForDashboard(ProviderId.Claude, force: true, active: ProviderId.Codex));
        Assert.True(UsageCoordinator.ShouldFetchForDashboard(ProviderId.Copilot, force: true, active: ProviderId.Codex));

        ProviderDiscoveryService.MarkExplicitlyDisabledForTesting(ProviderId.Claude);
        Assert.False(UsageCoordinator.ShouldFetchForDashboard(ProviderId.Claude, force: true, active: ProviderId.Claude));
    }

    private static UsageResult ClaudeResult()
        => UsageResult.Failure(ProviderId.Claude, "Synthetic usage result");

    private static IReadOnlyList<ProviderId> DisplayProviders(ProviderId? active)
        => UsageCoordinator.ComputeWidgetDisplayProviders(
            active, active.HasValue, Array.Empty<ProviderId>(), Enum.GetValues<ProviderId>(),
            WidgetSettingsService.IsProviderPinned, WidgetSettingsService.IsProviderVisible,
            ProviderDiscoveryService.IsEligible);

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
}
