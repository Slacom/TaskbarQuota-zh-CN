using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace TaskbarQuota;

public static class StartupSettingsService
{
    public sealed record StartupStatus(bool IsEnabled, bool DisabledByUser, bool HasOlderInstallerStartup);

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "TaskbarQuota";
    private const string LegacyRunValueName = "WinCheck";
    internal const string StartupTaskId = "TaskbarQuotaStartup";
    public const string StartupArgument = "--startup-widget";

    internal static bool HasPackageIdentity
    {
        get
        {
            try
            {
                return !string.IsNullOrEmpty(Package.Current.Id.FullName);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>Moves a WinCheck startup entry to TaskbarQuota after rename.</summary>
    public static void MigrateLegacyStartupEntryIfNeeded()
    {
        if (HasPackageIdentity)
            return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(LegacyRunValueName) is not string legacy
                || !legacy.Contains(StartupArgument, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
            if (key.GetValue(RunValueName) is null)
                ApplyRunKey(true);
        }
        catch
        {
        }
    }

    public static async Task<bool> IsEnabledAsync()
        => (await ReadStatusAsync()).IsEnabled;

    public static async Task<StartupStatus> ReadStatusAsync()
    {
        if (!HasPackageIdentity)
            return new StartupStatus(IsRunKeyEnabled, false, false);

        var task = await StartupTask.GetAsync(StartupTaskId);
        return new StartupStatus(IsEnabledState(task.State),
            task.State == StartupTaskState.DisabledByUser, HasOlderInstallerStartup());
    }

    private static bool HasOlderInstallerStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return IsOlderInstallerRunValue(key?.GetValue(RunValueName) as string, Environment.ProcessPath)
                || IsOlderInstallerRunValue(key?.GetValue(LegacyRunValueName) as string, Environment.ProcessPath);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsOlderInstallerRunValue(string? value, string? executable)
        => value is not null
            && value.Contains(StartupArgument, StringComparison.OrdinalIgnoreCase)
            && !IsRunValueForExecutable(value, executable);

    internal static bool IsEnabledState(StartupTaskState state)
        => state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    private static bool IsRunKeyEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(RunValueName) is string value
                    && IsRunValueForExecutable(value, Environment.ProcessPath);
            }
            catch
            {
                return false;
            }
        }
    }

    public static async Task<bool> ApplyAsync(bool enabled)
    {
        if (!HasPackageIdentity)
        {
            ApplyRunKey(enabled);
            return IsRunKeyEnabled;
        }

        var task = await StartupTask.GetAsync(StartupTaskId);
        if (enabled && task.State == StartupTaskState.Disabled)
            return IsEnabledState(await task.RequestEnableAsync());
        if (!enabled && task.State == StartupTaskState.Enabled)
            task.Disable();
        return IsEnabledState((await StartupTask.GetAsync(StartupTaskId)).State);
    }

    internal static bool IsRunValueForExecutable(string value, string? executable)
        => !string.IsNullOrWhiteSpace(executable)
            && string.Equals(value.Trim(), $"\"{executable}\" {StartupArgument}", StringComparison.OrdinalIgnoreCase);

    private static void ApplyRunKey(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
                return;

            if (!enabled)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                return;

            key.SetValue(RunValueName, $"\"{executable}\" {StartupArgument}");
        }
        catch
        {
            // Startup registration is best-effort; the app itself should continue normally.
        }
    }
}
