using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaskbarQuota.Usage;

namespace TaskbarQuota;

/// <summary>
/// Tracks which providers are probed, configured, and visible in the dashboard vs taskbar widget.
///
/// Display rules:
/// <list type="bullet">
///   <item>Explicitly disabled → never shown or fetched anywhere. Only the Settings
///   toggle opts back in.</item>
///   <item>Explicitly enabled, pinned, configured, or installed → eligible even when inactive.
///   Surface visibility preferences still decide where a provider appears.</item>
///   <item>With automatic hiding off, saved visible providers remain eligible. Unknown defaults
///   alone do not opt every provider into background requests.</item>
///   <item>Installed → shown and fetched even while its app is closed.</item>
/// </list>
/// </summary>
public static class ProviderDiscoveryService
{
    private static readonly object SyncRoot = new();
    private static string StatePath =>
        Path.Combine(AppStorage.AppDataDirectory, "provider-discovery.json");

    private static readonly HashSet<ProviderId> Probed = new();
    private static readonly HashSet<ProviderId> Configured = new();
    private static readonly HashSet<ProviderId> ExplicitlyEnabled = new();
    private static readonly HashSet<ProviderId> ExplicitlyDisabled = new();
    private static readonly HashSet<ProviderId> ExplicitlyWidgetDisabled = new();
    private static bool _savePending;

    static ProviderDiscoveryService() => Load();

    public static event Action<ProviderId>? Changed;

    /// <summary>
    /// Scans every provider installation at startup and restores installed or configured providers
    /// to the dashboard and widget. Explicit disables and widget-level hides remain user choices.
    /// </summary>
    public static void SyncInstalledProviderVisibility()
    {
        ProviderInstallDetector.WarmCliCache();

        lock (SyncRoot)
        {
            bool widgetChanged = false;
            bool dashboardChanged = false;
            foreach (ProviderId id in Enum.GetValues<ProviderId>())
            {
                if (ExplicitlyDisabled.Contains(id))
                    continue;

                // A configured provider has proven it is set up even when detection misses it, so it
                // recovers from an earlier automatic hide; otherwise eligibility could never show it.
                if (ProviderInstallDetector.IsInstalled(id) || Configured.Contains(id))
                {
                    dashboardChanged |= WidgetSettingsService.SetProviderDashboardVisibleSilent(id, true);
                    if (!ExplicitlyWidgetDisabled.Contains(id))
                        widgetChanged |= WidgetSettingsService.SetProviderVisibleSilent(id, true);
                    continue;
                }

                if (IsEligible(id))
                    continue;

                widgetChanged |= WidgetSettingsService.SetProviderVisibleSilent(id, false);
                dashboardChanged |= WidgetSettingsService.SetProviderDashboardVisibleSilent(id, false);
            }

            if (widgetChanged)
                WidgetSettingsService.SaveProviderVisibilityAndNotify();

            if (dashboardChanged)
                WidgetSettingsService.SaveDashboardProviderVisibilityAndNotify();
        }
    }

    public static void RecordFetchResult(UsageResult result)
    {
        bool stateChanged;
        bool widgetChanged = false;
        bool dashboardChanged = false;
        lock (SyncRoot)
        {
            stateChanged = Probed.Add(result.Id);

            if (result.Ok || result.ErrorKind == ProviderErrorKind.AuthRequired)
                stateChanged |= Configured.Add(result.Id);

            // Fetches update discovery state only. Startup sync restores installed
            // dashboard and widget entries; an explicit widget hide is never overridden.

            if (result.ErrorKind == ProviderErrorKind.NotInstalled
                && !ProviderInstallDetector.IsInstalled(result.Id)
                && WidgetSettingsService.AutoHideUnavailable
                && !ExplicitlyEnabled.Contains(result.Id)
                && !WidgetSettingsService.IsProviderPinned(result.Id))
            {
                dashboardChanged = WidgetSettingsService.SetProviderDashboardVisibleSilent(result.Id, false);
                widgetChanged = WidgetSettingsService.SetProviderVisibleSilent(result.Id, false);
            }

            if (stateChanged || _savePending)
                Save();
            // Keep writes from concurrent fetch completions serialized with their mutations.
            if (widgetChanged)
                WidgetSettingsService.SaveProviderVisibility();
            if (dashboardChanged)
                WidgetSettingsService.SaveDashboardProviderVisibility();
        }

        // UI subscribers can read discovery state; never notify them while holding its lock.
        if (widgetChanged)
            WidgetSettingsService.NotifyProviderVisibilityChanged();
        if (dashboardChanged)
            WidgetSettingsService.NotifyDashboardProviderVisibilityChanged();
        if (stateChanged)
            Changed?.Invoke(result.Id);
    }

    public static bool IsProbed(ProviderId id)
    {
        lock (SyncRoot)
            return Probed.Contains(id);
    }

    public static bool IsConfigured(ProviderId id)
    {
        lock (SyncRoot)
            return Configured.Contains(id);
    }

    public static bool IsExplicitlyEnabled(ProviderId id)
    {
        lock (SyncRoot)
            return ExplicitlyEnabled.Contains(id);
    }

    public static bool IsExplicitlyDisabled(ProviderId id)
    {
        lock (SyncRoot)
            return ExplicitlyDisabled.Contains(id);
    }

    public static void EnableProvider(ProviderId id)
    {
        lock (SyncRoot)
        {
            ExplicitlyEnabled.Add(id);
            ExplicitlyDisabled.Remove(id);
            // A widget-level hide (SetWidgetVisibilityPreference) is a separate user choice:
            // enabling the provider in Settings must not re-show a tile the user hid, while
            // the dashboard card follows the enable.
            bool widgetChanged = false;
            if (!ExplicitlyWidgetDisabled.Contains(id))
                widgetChanged = WidgetSettingsService.SetProviderVisibleSilent(id, true);
            bool dashboardChanged = WidgetSettingsService.SetProviderDashboardVisibleSilent(id, true);
            if (widgetChanged)
                WidgetSettingsService.SaveProviderVisibilityAndNotify();
            if (dashboardChanged)
                WidgetSettingsService.SaveDashboardProviderVisibilityAndNotify();
            Save();
        }
    }

    /// <summary>
    /// Records a user widget-visibility choice separately from automatic provider discovery, so an
    /// installed provider that the user hid is not silently restored on the next launch.
    /// </summary>
    public static void SetWidgetVisibilityPreference(ProviderId id, bool visible)
    {
        lock (SyncRoot)
        {
            if (visible)
                ExplicitlyWidgetDisabled.Remove(id);
            else
                ExplicitlyWidgetDisabled.Add(id);

            WidgetSettingsService.SetProviderVisible(id, visible);
            Save();
        }
    }

    public static void DisableProvider(ProviderId id)
    {
        lock (SyncRoot)
        {
            ExplicitlyEnabled.Remove(id);
            ExplicitlyDisabled.Add(id);
            WidgetSettingsService.SetProviderDashboardVisible(id, false);
            WidgetSettingsService.SetProviderVisible(id, false);
            Save();
        }
    }

    public static bool ShouldFetch(ProviderId id, ProviderId? active)
    {
        if (IsExplicitlyDisabled(id))
            return false;
        if (id == active)
            return true;
        if (!WidgetSettingsService.IsProviderDashboardVisible(id) && !WidgetSettingsService.IsProviderVisible(id))
            return false;
        return IsEligible(id);
    }

    public static bool ShouldShowInDashboard(UsageResult result, ProviderId? active)
    {
        if (IsExplicitlyDisabled(result.Id))
            return false;
        if (result.Id == active)
            return true;
        if (!WidgetSettingsService.IsProviderDashboardVisible(result.Id))
            return false;
        return IsEligible(result.Id);
    }

    /// <summary>
    /// Shared by dashboard fetching and widget availability so changing focus cannot discard
    /// an explicit provider choice merely because installation detection missed it.
    /// </summary>
    internal static bool IsEligible(ProviderId id)
    {
        if (IsExplicitlyDisabled(id))
            return false;
        if (IsExplicitlyEnabled(id) || WidgetSettingsService.IsProviderPinned(id)
            || IsConfigured(id) || ProviderInstallDetector.IsInstalled(id))
            return true;

        // Default visibility is true for every registered provider. Only a saved visible choice
        // should bypass availability when automatic hiding is off, not those defaults.
        return !WidgetSettingsService.AutoHideUnavailable
            && ((WidgetSettingsService.TryGetDashboardProviderVisibilityOverride(id, out bool dashboard) && dashboard)
                || (WidgetSettingsService.TryGetProviderVisibilityOverride(id, out bool widget) && widget));
    }

    public static bool ShouldShowInAvailable(UsageResult result, ProviderId? active)
    {
        // Installed providers appear directly in the dashboard. The Available
        // pipeline stays inert; not-installed providers remain in Settings.
        _ = result;
        _ = active;
        return false;
    }

    internal static void ResetForTesting()
    {
        lock (SyncRoot)
        {
            Probed.Clear();
            Configured.Clear();
            ExplicitlyEnabled.Clear();
            ExplicitlyDisabled.Clear();
            ExplicitlyWidgetDisabled.Clear();
            _savePending = false;
        }
    }

    internal static void MarkProbedForTesting(ProviderId id)
    {
        lock (SyncRoot)
            Probed.Add(id);
    }

    internal static void MarkConfiguredForTesting(ProviderId id)
    {
        lock (SyncRoot)
            Configured.Add(id);
    }

    internal static void MarkExplicitlyDisabledForTesting(ProviderId id)
    {
        lock (SyncRoot)
            ExplicitlyDisabled.Add(id);
    }

    internal static void MarkExplicitlyWidgetDisabledForTesting(ProviderId id)
    {
        lock (SyncRoot)
            ExplicitlyWidgetDisabled.Add(id);
    }

    private static void Load()
    {
        bool hasExplicitWidgetState = false;
        try
        {
            if (File.Exists(StatePath))
            {
                var state = JsonSerializer.Deserialize<DiscoveryState>(File.ReadAllText(StatePath));
                if (state is not null)
                {
                    foreach (var id in state.Probed ?? [])
                        if (Enum.TryParse<ProviderId>(id, out var parsed))
                            Probed.Add(parsed);

                    foreach (var id in state.Configured ?? [])
                        if (Enum.TryParse<ProviderId>(id, out var parsed))
                            Configured.Add(parsed);

                    foreach (var id in state.ExplicitlyEnabled ?? [])
                        if (Enum.TryParse<ProviderId>(id, out var parsed))
                            ExplicitlyEnabled.Add(parsed);

                    foreach (var id in state.ExplicitlyDisabled ?? [])
                        if (Enum.TryParse<ProviderId>(id, out var parsed))
                            ExplicitlyDisabled.Add(parsed);

                    foreach (var id in state.ExplicitlyWidgetDisabled ?? [])
                        if (Enum.TryParse<ProviderId>(id, out var parsed))
                            ExplicitlyWidgetDisabled.Add(parsed);

                    hasExplicitWidgetState = state.ExplicitlyWidgetDisabled is not null;
                }
            }
        }
        catch
        {
            // Best effort — rediscover on next fetch.
        }

        if (hasExplicitWidgetState)
            return;

        // v1.2/v1.3 stored widget visibility but did not record that a false value came from the
        // user. Migrate only widget-only hides. Automatic discovery hides both dashboard and
        // widget, so those remain eligible to reappear if the provider is installed later.
        bool migrated = false;
        foreach (ProviderId id in Enum.GetValues<ProviderId>())
        {
            bool widgetHidden = WidgetSettingsService.TryGetProviderVisibilityOverride(id, out bool widgetVisible)
                && !widgetVisible;
            bool dashboardHidden = WidgetSettingsService.TryGetDashboardProviderVisibilityOverride(id, out bool dashboardVisible)
                && !dashboardVisible;
            if (widgetHidden && !dashboardHidden)
                migrated |= ExplicitlyWidgetDisabled.Add(id);
        }

        if (migrated)
            Save();
    }

    private static void Save()
    {
        _savePending = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var state = new DiscoveryState
            {
                Probed = Probed.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                Configured = Configured.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                ExplicitlyEnabled = ExplicitlyEnabled.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                ExplicitlyDisabled = ExplicitlyDisabled.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                ExplicitlyWidgetDisabled = ExplicitlyWidgetDisabled.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
            };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            _savePending = false;
        }
        catch
        {
            // Best effort.
        }
    }

    private sealed class DiscoveryState
    {
        public string[]? Probed { get; set; }
        public string[]? Configured { get; set; }
        public string[]? ExplicitlyEnabled { get; set; }
        public string[]? ExplicitlyDisabled { get; set; }
        public string[]? ExplicitlyWidgetDisabled { get; set; }
    }
}
