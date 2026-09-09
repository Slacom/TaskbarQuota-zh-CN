using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaskbarQuota.Usage;

namespace TaskbarQuota;

/// <summary>
/// Tracks which providers are probed, configured, and visible in the dashboard vs taskbar widget.
/// Auto-hides providers that are not installed or require authentication unless the user explicitly enables them.
/// </summary>
public static class ProviderDiscoveryService
{
    private static readonly object SyncRoot = new();
    private static readonly string StatePath =
        Path.Combine(AppStorage.AppDataDirectory, "provider-discovery.json");

    private static readonly HashSet<ProviderId> Probed = new();
    private static readonly HashSet<ProviderId> Configured = new();
    private static readonly HashSet<ProviderId> KnownNotInstalled = new();
    private static readonly HashSet<ProviderId> KnownAuthRequired = new();
    private static readonly HashSet<ProviderId> AutoHiddenUnavailable = new();
    private static readonly HashSet<ProviderId> ExplicitlyEnabled = new();
    private static readonly HashSet<ProviderId> ExplicitlyDisabled = new();
    private static readonly HashSet<ProviderId> ExplicitlyWidgetDisabled = new();

    static ProviderDiscoveryService() => Load();

    /// <summary>
    /// Ensures every detected installed provider is visible in the dashboard and widget unless the user hid it.
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
                if (!ProviderInstallDetector.IsInstalled(id) || ExplicitlyDisabled.Contains(id))
                    continue;

                KnownNotInstalled.Remove(id);
                AutoHiddenUnavailable.Remove(id);
                widgetChanged |= TryEnableProviderSilent(id, out bool dashChanged);
                dashboardChanged |= dashChanged;
            }

            if (widgetChanged)
                WidgetSettingsService.SaveProviderVisibilityAndNotify();

            if (dashboardChanged)
                WidgetSettingsService.SaveDashboardProviderVisibilityAndNotify();
        }
    }

    public static void RecordFetchResult(UsageResult result)
    {
        lock (SyncRoot)
        {
            Probed.Add(result.Id);

            bool installed = ProviderInstallDetector.IsInstalled(result.Id);
            if (installed && !ExplicitlyDisabled.Contains(result.Id))
                EnsureProviderEnabled(result.Id);

            if (installed || result.Ok || result.ErrorKind == ProviderErrorKind.NotRunning)
            {
                KnownNotInstalled.Remove(result.Id);
                KnownAuthRequired.Remove(result.Id);
                AutoHiddenUnavailable.Remove(result.Id);
            }
            else if (result.ErrorKind == ProviderErrorKind.NotInstalled)
            {
                KnownNotInstalled.Add(result.Id);
                KnownAuthRequired.Remove(result.Id);
            }
            else if (result.ErrorKind == ProviderErrorKind.AuthRequired)
            {
                KnownAuthRequired.Add(result.Id);
                KnownNotInstalled.Remove(result.Id);
            }

            bool newlyConfigured = result.Ok && !Configured.Contains(result.Id);

            if (result.Ok || result.ErrorKind == ProviderErrorKind.AuthRequired)
                Configured.Add(result.Id);

            if (!ExplicitlyDisabled.Contains(result.Id) && result.Ok)
            {
                if (!WidgetSettingsService.IsProviderDashboardVisible(result.Id))
                    WidgetSettingsService.SetProviderDashboardVisible(result.Id, true);

            // First successful fetch after install/login — restore widget visibility
            // (auto-hide turns it off for NotInstalled, but usage should return once set up).
                if (newlyConfigured
                    && !ExplicitlyWidgetDisabled.Contains(result.Id)
                    && !WidgetSettingsService.IsProviderVisible(result.Id))
                    WidgetSettingsService.SetProviderVisible(result.Id, true);
            }

            if (result.ErrorKind == ProviderErrorKind.NotRunning
                && ProviderInstallDetector.IsInstalled(result.Id)
                && !ExplicitlyDisabled.Contains(result.Id))
            {
                if (!WidgetSettingsService.IsProviderDashboardVisible(result.Id))
                    WidgetSettingsService.SetProviderDashboardVisible(result.Id, true);
                if (!ExplicitlyWidgetDisabled.Contains(result.Id)
                    && !WidgetSettingsService.IsProviderVisible(result.Id))
                    WidgetSettingsService.SetProviderVisible(result.Id, true);
            }

            if (result.ErrorKind is ProviderErrorKind.NotInstalled or ProviderErrorKind.AuthRequired
                && WidgetSettingsService.AutoHideUnavailable
                && !ExplicitlyEnabled.Contains(result.Id)
                && !ExplicitlyDisabled.Contains(result.Id))
            {
                AutoHiddenUnavailable.Add(result.Id);
                SetAutomaticVisibility(result.Id, dashboardVisible: false, widgetVisible: false);
            }

            Save();
        }
    }

    /// <summary>
    /// Applies a changed auto-hide setting to providers whose last probe explicitly reported that they
    /// are not installed. Only visibility changed by this policy is restored; explicit dashboard/widget
    /// choices remain authoritative.
    /// </summary>
    public static void ReconcileUnavailableVisibility()
    {
        lock (SyncRoot)
        {
            var candidates = KnownNotInstalled
                .Concat(KnownAuthRequired)
                .Concat(AutoHiddenUnavailable)
                .Distinct()
                .ToArray();

            foreach (var id in candidates)
            {
                bool installed = ProviderInstallDetector.IsInstalled(id);
                if (ExplicitlyEnabled.Contains(id) || ExplicitlyDisabled.Contains(id))
                {
                    KnownNotInstalled.Remove(id);
                    KnownAuthRequired.Remove(id);
                    AutoHiddenUnavailable.Remove(id);
                    continue;
                }

                if (installed && !KnownAuthRequired.Contains(id))
                    KnownNotInstalled.Remove(id);

                bool knownUnavailable = KnownNotInstalled.Contains(id) || KnownAuthRequired.Contains(id);

                if (WidgetSettingsService.AutoHideUnavailable && knownUnavailable)
                {
                    AutoHiddenUnavailable.Add(id);
                    SetAutomaticVisibility(id, dashboardVisible: false, widgetVisible: false);
                }
                else if (AutoHiddenUnavailable.Remove(id))
                {
                    SetAutomaticVisibility(
                        id,
                        dashboardVisible: !ExplicitlyDisabled.Contains(id),
                        widgetVisible: !ExplicitlyWidgetDisabled.Contains(id));
                }
            }

            Save();
        }
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

    private static bool HasExplicitDashboardEnable(ProviderId id)
        => WidgetSettingsService.TryGetDashboardProviderVisibilityOverride(id, out bool visible) && visible;

    public static void EnableProvider(ProviderId id)
    {
        bool disableAutoHide;
        lock (SyncRoot)
        {
            disableAutoHide = WidgetSettingsService.AutoHideUnavailable
                && (KnownNotInstalled.Contains(id)
                    || KnownAuthRequired.Contains(id)
                    || AutoHiddenUnavailable.Contains(id));
            ExplicitlyEnabled.Add(id);
            ExplicitlyDisabled.Remove(id);
            KnownNotInstalled.Remove(id);
            KnownAuthRequired.Remove(id);
            AutoHiddenUnavailable.Remove(id);
            EnsureProviderEnabled(id);
            Save();
        }

        // A manual dashboard enable is an explicit request to see this unavailable provider. The
        // global filter must yield, otherwise the next discovery pass would immediately hide it again.
        if (disableAutoHide)
            WidgetSettingsService.ApplyAutoHideUnavailable(false);
    }

    /// <summary>
    /// Records a user widget-visibility choice separately from automatic provider discovery, so an
    /// installed provider that the user hid is not silently restored on the next launch.
    /// </summary>
    public static void SetWidgetVisibilityPreference(ProviderId id, bool visible)
    {
        lock (SyncRoot)
        {
            AutoHiddenUnavailable.Remove(id);
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
            KnownNotInstalled.Remove(id);
            KnownAuthRequired.Remove(id);
            AutoHiddenUnavailable.Remove(id);
            WidgetSettingsService.SetProviderDashboardVisible(id, false);
            WidgetSettingsService.SetProviderVisible(id, false);
            Save();
        }
    }

    public static bool ShouldFetch(ProviderId id, ProviderId? active)
    {
        if (id == active)
            return true;
        if (IsExplicitlyDisabled(id) && !HasExplicitDashboardEnable(id))
            return false;
        if (ProviderInstallDetector.IsInstalled(id))
            return true;
        if (!IsProbed(id))
            return true;
        if (WidgetSettingsService.IsProviderDashboardVisible(id))
            return true;
        if (WidgetSettingsService.IsProviderVisible(id))
            return true;
        if (IsExplicitlyEnabled(id))
            return true;
        if (IsConfigured(id))
            return true;
        return false;
    }

    public static bool ShouldShowInDashboard(UsageResult result, ProviderId? active)
    {
        if (IsExplicitlyDisabled(result.Id) && !HasExplicitDashboardEnable(result.Id))
            return false;
        if (ProviderInstallDetector.IsInstalled(result.Id))
            return true;
        if (result.Id == active)
            return true;
        if (WidgetSettingsService.IsProviderDashboardVisible(result.Id))
            return true;
        if (IsExplicitlyEnabled(result.Id))
            return true;
        if (result.Ok)
            return true;
        if (result.ErrorKind == ProviderErrorKind.AuthRequired)
            return true;
        if (result.ErrorKind == ProviderErrorKind.NotRunning)
            return true;
        if (IsConfigured(result.Id) && result.ErrorKind is not ProviderErrorKind.NotInstalled)
            return true;
        return false;
    }

    public static bool ShouldShowInAvailable(UsageResult result, ProviderId? active)
    {
        if (ShouldShowInDashboard(result, active))
            return false;

        return result.ErrorKind == ProviderErrorKind.NotInstalled
            || (!result.Ok && !IsConfigured(result.Id));
    }

    internal static void ResetForTesting()
    {
        lock (SyncRoot)
        {
            Probed.Clear();
            Configured.Clear();
            KnownNotInstalled.Clear();
            KnownAuthRequired.Clear();
            AutoHiddenUnavailable.Clear();
            ExplicitlyEnabled.Clear();
            ExplicitlyDisabled.Clear();
            ExplicitlyWidgetDisabled.Clear();
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

    private static void SetAutomaticVisibility(ProviderId id, bool dashboardVisible, bool widgetVisible)
    {
        bool widgetChanged = WidgetSettingsService.SetProviderVisibleSilent(id, widgetVisible);
        bool dashboardChanged = WidgetSettingsService.SetProviderDashboardVisibleSilent(id, dashboardVisible);

        // Automatic discovery must not go through SetProviderVisible(false), because that public method
        // intentionally clears a user pin. Auto-hide is reversible policy state, so the pin survives and
        // returns with the provider when the policy is turned off or the provider becomes available.
        if (widgetChanged)
            WidgetSettingsService.SaveProviderVisibilityAndNotify();
        if (dashboardChanged)
            WidgetSettingsService.SaveDashboardProviderVisibilityAndNotify();
    }

    private static void EnsureProviderEnabled(ProviderId id)
    {
        if (TryEnableProviderSilent(id, out bool dashboardChanged))
            WidgetSettingsService.SaveProviderVisibilityAndNotify();

        if (dashboardChanged)
            WidgetSettingsService.SaveDashboardProviderVisibilityAndNotify();
    }

    private static bool TryEnableProviderSilent(ProviderId id, out bool dashboardChanged)
    {
        dashboardChanged = false;
        bool widgetChanged = false;

        if (!WidgetSettingsService.IsProviderDashboardVisible(id))
        {
            WidgetSettingsService.SetProviderDashboardVisibleSilent(id, true);
            dashboardChanged = true;
        }

        if (!ExplicitlyWidgetDisabled.Contains(id)
            && !WidgetSettingsService.IsProviderVisible(id))
        {
            WidgetSettingsService.SetProviderVisibleSilent(id, true);
            widgetChanged = true;
        }

        return widgetChanged;
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

                    foreach (var id in state.KnownNotInstalled ?? [])
                        if (Enum.TryParse<ProviderId>(id, out var parsed))
                            KnownNotInstalled.Add(parsed);

                    foreach (var id in state.KnownAuthRequired ?? [])
                        if (Enum.TryParse<ProviderId>(id, out var parsed))
                            KnownAuthRequired.Add(parsed);

                    foreach (var id in state.AutoHiddenUnavailable ?? [])
                        if (Enum.TryParse<ProviderId>(id, out var parsed))
                            AutoHiddenUnavailable.Add(parsed);

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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var state = new DiscoveryState
            {
                Probed = Probed.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                Configured = Configured.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                KnownNotInstalled = KnownNotInstalled.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                KnownAuthRequired = KnownAuthRequired.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                AutoHiddenUnavailable = AutoHiddenUnavailable.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                ExplicitlyEnabled = ExplicitlyEnabled.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                ExplicitlyDisabled = ExplicitlyDisabled.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
                ExplicitlyWidgetDisabled = ExplicitlyWidgetDisabled.Select(id => id.ToString()).OrderBy(s => s).ToArray(),
            };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
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
        public string[]? KnownNotInstalled { get; set; }
        public string[]? KnownAuthRequired { get; set; }
        public string[]? AutoHiddenUnavailable { get; set; }
        public string[]? ExplicitlyEnabled { get; set; }
        public string[]? ExplicitlyDisabled { get; set; }
        public string[]? ExplicitlyWidgetDisabled { get; set; }
    }
}
