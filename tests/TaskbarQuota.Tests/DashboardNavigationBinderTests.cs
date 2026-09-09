using TaskbarQuota.Taskbar;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

public sealed class DashboardNavigationBinderTests
{
    private static IReadOnlySet<ProviderId> Set(params ProviderId[] ids)
        => ids.ToHashSet();

    [Fact]
    public void FreshUserWithAutoHideOff_KeepsEverySupportedProviderNavigationEntry()
    {
        var all = Enum.GetValues<ProviderId>();

        var actual = DashboardNavigationBinder.ComputeNavigationProviderIds(
            all,
            dashboardProviders: all.ToHashSet(),
            availableProviders: Set(),
            isDashboardVisible: _ => true,
            hideUnavailable: false);

        Assert.Equal(all, actual);
    }

    [Fact]
    public void ComputeNavigationProviderIds_ExcludesDashboardDisabledProviderEvenWithDashboard()
    {
        var actual = DashboardNavigationBinder.ComputeNavigationProviderIds(
            [ProviderId.Codex, ProviderId.Claude],
            dashboardProviders: Set(ProviderId.Codex, ProviderId.Claude),
            availableProviders: Set(),
            isDashboardVisible: id => id == ProviderId.Codex,
            hideUnavailable: false);

        Assert.Equal([ProviderId.Codex], actual);
    }

    [Fact]
    public void ComputeNavigationProviderIds_AutoHideRemovesProvidersWithoutDashboardCard()
    {
        var actual = DashboardNavigationBinder.ComputeNavigationProviderIds(
            [ProviderId.Codex, ProviderId.Grok, ProviderId.Cursor],
            dashboardProviders: Set(ProviderId.Codex),
            availableProviders: Set(ProviderId.Grok, ProviderId.Cursor),
            isDashboardVisible: _ => true,
            hideUnavailable: true);

        Assert.Equal([ProviderId.Codex], actual);
    }

    [Fact]
    public void ComputeNavigationProviderIds_WhenAutoHideOffKeepsSetupEntryButNoGhostEntry()
    {
        var actual = DashboardNavigationBinder.ComputeNavigationProviderIds(
            [ProviderId.Codex, ProviderId.Grok, ProviderId.Cursor],
            dashboardProviders: Set(ProviderId.Codex),
            availableProviders: Set(ProviderId.Grok),
            isDashboardVisible: _ => true,
            hideUnavailable: false);

        Assert.Equal([ProviderId.Codex, ProviderId.Grok], actual);
    }

    [Fact]
    public void ComputeNavigationProviderIds_ExcludesProviderWithNoBackingCardEvenWhenVisible()
    {
        var actual = DashboardNavigationBinder.ComputeNavigationProviderIds(
            [ProviderId.Codex, ProviderId.Claude],
            dashboardProviders: Set(ProviderId.Codex),
            availableProviders: Set(),
            isDashboardVisible: _ => true,
            hideUnavailable: false);

        Assert.Equal([ProviderId.Codex], actual);
    }
}
