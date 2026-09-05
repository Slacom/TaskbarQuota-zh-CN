using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TaskbarQuota.Diagnostics;
using TaskbarQuota.Interop;

namespace TaskbarQuota.Taskbar
{
    internal readonly record struct DisplayIdentity(string DisplayKey, string GdiDeviceName, bool IsPrimary);

    internal readonly record struct TaskbarWindowTarget(
        IntPtr Handle,
        bool IsPrimary,
        string DisplayKey,
        string GdiDeviceName = "",
        IntPtr Monitor = default,
        RECT Bounds = default)
    {
        internal const string PrimaryClassName = "Shell_TrayWnd";
        internal const string SecondaryClassName = "Shell_SecondaryTrayWnd";

        public int DisplayNumber
        {
            get
            {
                int fromGdi = TryGetDisplayNumber(GdiDeviceName);
                return fromGdi > 0 ? fromGdi : TryGetDisplayNumber(DisplayKey);
            }
        }

        public DisplayIdentity ToIdentity() => new(DisplayKey, GdiDeviceName, IsPrimary);

        public static bool TryFindAll(out IReadOnlyList<TaskbarWindowTarget> result)
        {
            var targets = new List<TaskbarWindowTarget>();
            var gc = GCHandle.Alloc(targets);
            bool success;
            try
            {
                success = User32.EnumWindows(EnumTaskbarWindow, GCHandle.ToIntPtr(gc));
            }
            finally
            {
                gc.Free();
            }

            var canonical = SelectCanonicalTaskbars(targets);
            result = OrderForDisplay(canonical);
            return success;
        }

        public string GetPositionPath()
            => GetPositionPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarQuota"));

        internal string GetPositionPath(string directory)
        {
            string path = Path.Combine(directory, BuildPositionFileName(DisplayKey));

            string gdiKey = SanitizeDisplayToken(GdiDeviceName);
            if (gdiKey.Length > 0 && !string.Equals(gdiKey, DisplayKey, StringComparison.OrdinalIgnoreCase))
                MigratePositionFiles(Path.Combine(directory, BuildPositionFileName(gdiKey)), path);

            if (IsPrimary)
                MigratePositionFiles(Path.Combine(directory, "taskbar-widget-position.txt"), path);

            return path;
        }

        internal static bool IsTaskbarClassName(string className, out bool isPrimary)
        {
            isPrimary = string.Equals(className, PrimaryClassName, StringComparison.Ordinal);
            return isPrimary || string.Equals(className, SecondaryClassName, StringComparison.Ordinal);
        }

        internal static string SanitizeDisplayToken(string? value)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(value))
            {
                foreach (char c in value)
                {
                    if (char.IsLetterOrDigit(c) || c is '-' or '_')
                        builder.Append(c);
                }
            }

            return builder.ToString();
        }

        internal static string BuildDisplayKey(string? displayId, RECT bounds)
            => BuildDisplayKey(displayId, stableMonitorId: null, bounds);

        internal static string BuildDisplayKey(string? displayId, string? stableMonitorId, RECT bounds)
        {
            string stable = SanitizeDisplayToken(stableMonitorId);
            if (stable.Length > 0)
                return stable;

            string sanitized = SanitizeDisplayToken(displayId);
            return sanitized.Length > 0
                ? sanitized
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{bounds.left}_{bounds.top}_{bounds.right}_{bounds.bottom}");
        }

        internal static string BuildPositionFileName(string displayKey)
            => $"taskbar-widget-position-{displayKey}.txt";

        internal static int TryGetDisplayNumber(string? displayKey)
        {
            const string prefix = "DISPLAY";
            if (string.IsNullOrWhiteSpace(displayKey)
                || !displayKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return int.TryParse(
                displayKey.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int number)
                    ? number
                    : 0;
        }

        internal static string FormatScreenLabel(int ordinal, bool isPrimary, bool includePrimarySuffix = true)
        {
            string label = $"Screen {Math.Max(1, ordinal)}";
            return includePrimarySuffix && isPrimary ? $"{label} (primary)" : label;
        }

        internal static string GetDisplayLabel(string displayKey)
            => GetDisplayLabel(displayKey, liveTargets: null);

        internal static string GetDisplayLabel(string displayKey, IReadOnlyList<TaskbarWindowTarget>? liveTargets)
        {
            if (displayKey == WidgetSettingsService.AllDisplaysPinDestination)
                return "all screens";

            if (liveTargets is { Count: > 0 })
            {
                var ordered = OrderForDisplay(liveTargets);
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (MatchesIdentity(ordered[i].ToIdentity(), displayKey))
                        return FormatScreenLabel(i + 1, ordered[i].IsPrimary, includePrimarySuffix: false);
                }
            }

            int number = TryGetDisplayNumber(displayKey);
            return number > 0 ? $"Screen {number}" : "this screen";
        }

        internal static IReadOnlyList<TaskbarWindowTarget> OrderForDisplay(
            IEnumerable<TaskbarWindowTarget> targets)
            => targets
                .OrderByDescending(target => target.IsPrimary)
                .ThenBy(target => target.Bounds.left)
                .ThenBy(target => target.Bounds.top)
                .ThenBy(target => target.DisplayKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Keeps one taskbar per monitor so leftover Explorer <c>Shell_SecondaryTrayWnd</c> windows
        /// do not appear as extra screens.
        /// </summary>
        internal static IReadOnlyList<TaskbarWindowTarget> SelectCanonicalTaskbars(
            IEnumerable<TaskbarWindowTarget> candidates)
        {
            var chosen = new List<TaskbarWindowTarget>();
            foreach (var group in candidates
                .Where(IsUsableCandidate)
                .GroupBy(target => target.Monitor))
            {
                chosen.Add(group
                    .OrderByDescending(target => target.IsPrimary)
                    .ThenByDescending(target => DisplayArea(target.Bounds))
                    .First());
            }

            return DisambiguateDisplayKeys(chosen);
        }

        /// <summary>
        /// Maps a persisted GDI name such as <c>DISPLAY2</c> onto the current monitor identity.
        /// Windows increments <c>\\.\DISPLAYn</c> across driver resets, so last session's
        /// DISPLAY2 is often today's DISPLAY7 on the same physical screen.
        /// </summary>
        internal static string ResolvePersistedDisplayKey(
            string? persisted,
            IReadOnlyList<DisplayIdentity> live)
        {
            if (string.IsNullOrWhiteSpace(persisted) || live.Count == 0)
                return persisted?.Trim() ?? string.Empty;

            string key = persisted.Trim();
            if (key == WidgetSettingsService.AllDisplaysPinDestination)
                return key;

            foreach (var item in live)
            {
                if (MatchesIdentity(item, key))
                    return item.DisplayKey;
            }

            int persistedNumber = TryGetDisplayNumber(key);
            if (persistedNumber == 1)
            {
                foreach (var item in live)
                {
                    if (item.IsPrimary)
                        return item.DisplayKey;
                }
            }

            if (persistedNumber > 1)
            {
                DisplayIdentity? secondary = null;
                foreach (var item in live)
                {
                    if (item.IsPrimary)
                        continue;
                    if (secondary is not null)
                        return key;
                    secondary = item;
                }

                if (secondary is { } onlySecondary)
                    return onlySecondary.DisplayKey;
            }

            return key;
        }

        internal static bool TryMigratePersistedKey(
            string persisted,
            IReadOnlyList<DisplayIdentity> live,
            out string resolved)
        {
            // The primary/sole-secondary heuristics are temporary routing fallbacks, not proof
            // of identity. Persisting one during an incomplete scan loses the original choice.
            resolved = persisted;
            foreach (var item in live)
            {
                if (MatchesIdentity(item, persisted.Trim()))
                {
                    resolved = item.DisplayKey;
                    return !string.Equals(resolved, persisted, StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        internal static string GetDisplayKeyForWindow(
            IntPtr hwnd,
            IReadOnlyList<TaskbarWindowTarget>? liveTargets = null)
        {
            if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
                return string.Empty;

            var monitor = User32.MonitorFromWindow(hwnd, MonitorFromFlags.MONITOR_DEFAULTTONULL);
            if (monitor == IntPtr.Zero)
                return string.Empty;

            if (liveTargets is null)
                TryFindAll(out liveTargets);
            string canonicalKey = GetDisplayKeyForMonitor(monitor, liveTargets);
            if (canonicalKey.Length > 0)
                return canonicalKey;

            var info = MONITORINFOEX.Create();
            if (!User32.GetMonitorInfo(monitor, ref info))
                return string.Empty;

            return BuildDisplayKey(
                info.szDevice,
                TryGetMonitorDeviceId(info.szDevice),
                info.rcMonitor);
        }

        internal static string GetDisplayKeyForMonitor(
            IntPtr monitor,
            IReadOnlyList<TaskbarWindowTarget> liveTargets)
        {
            if (monitor != IntPtr.Zero)
            {
                foreach (var target in liveTargets)
                {
                    if (target.Monitor == monitor)
                        return target.DisplayKey;
                }
            }

            return string.Empty;
        }

        internal static string TryGetMonitorDeviceId(string? gdiDeviceName)
        {
            if (string.IsNullOrWhiteSpace(gdiDeviceName))
                return string.Empty;

            var device = DISPLAY_DEVICE.Create();
            return User32.EnumDisplayDevices(gdiDeviceName, 0, ref device, 0)
                ? SanitizeDisplayToken(device.DeviceID)
                : string.Empty;
        }

        private static bool MatchesIdentity(DisplayIdentity identity, string key)
            => string.Equals(identity.DisplayKey, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(identity.GdiDeviceName, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    SanitizeDisplayToken(identity.GdiDeviceName),
                    SanitizeDisplayToken(key),
                    StringComparison.OrdinalIgnoreCase);

        private static bool IsUsableCandidate(TaskbarWindowTarget target)
            => target.Monitor != IntPtr.Zero
                && target.Bounds.right > target.Bounds.left
                && target.Bounds.bottom > target.Bounds.top;

        private static int DisplayArea(RECT bounds)
            => Math.Max(0, bounds.right - bounds.left) * Math.Max(0, bounds.bottom - bounds.top);

        internal static IReadOnlyList<TaskbarWindowTarget> DisambiguateDisplayKeys(
            IReadOnlyList<TaskbarWindowTarget> targets)
        {
            if (targets.Count < 2)
                return targets;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
                counts[target.DisplayKey] = counts.GetValueOrDefault(target.DisplayKey) + 1;

            bool anyDuplicate = false;
            foreach (int count in counts.Values)
            {
                if (count > 1)
                {
                    anyDuplicate = true;
                    break;
                }
            }

            if (!anyDuplicate)
                return targets;

            var unique = new List<TaskbarWindowTarget>(targets.Count);
            foreach (var target in targets)
            {
                if (counts[target.DisplayKey] == 1)
                {
                    unique.Add(target);
                    continue;
                }

                string suffix = SanitizeDisplayToken(target.GdiDeviceName);
                if (suffix.Length == 0)
                {
                    suffix = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{target.Bounds.left}_{target.Bounds.top}");
                }

                unique.Add(target with { DisplayKey = $"{target.DisplayKey}_{suffix}" });
            }

            return unique;
        }

        private static bool EnumTaskbarWindow(IntPtr hwnd, IntPtr lParam)
        {
            var builder = new StringBuilder(64);
            User32.GetClassName(hwnd, builder, builder.Capacity);
            if (!IsTaskbarClassName(builder.ToString(), out bool isPrimary)
                || !User32.IsWindow(hwnd)
                || IsCloaked(hwnd)
                || GCHandle.FromIntPtr(lParam).Target is not List<TaskbarWindowTarget> targets)
            {
                return true;
            }

            var monitor = User32.MonitorFromWindow(hwnd, MonitorFromFlags.MONITOR_DEFAULTTONULL);
            if (monitor == IntPtr.Zero)
                return true;

            var bounds = GetBounds(hwnd);
            if (bounds.right <= bounds.left || bounds.bottom <= bounds.top)
                return true;

            var info = MONITORINFOEX.Create();
            string gdiDevice = User32.GetMonitorInfo(monitor, ref info)
                ? info.szDevice
                : string.Empty;
            var keyBounds = info.rcMonitor.right > info.rcMonitor.left ? info.rcMonitor : bounds;
            targets.Add(new TaskbarWindowTarget(
                hwnd,
                isPrimary,
                BuildDisplayKey(gdiDevice, TryGetMonitorDeviceId(gdiDevice), keyBounds),
                SanitizeDisplayToken(gdiDevice),
                monitor,
                bounds));
            return true;
        }

        private static bool IsCloaked(IntPtr hwnd)
            => DwmApi.DwmGetWindowAttribute(hwnd, DwmApi.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0;

        private static RECT GetBounds(IntPtr hwnd)
            => User32.GetWindowRect(hwnd, out var bounds) ? bounds : default;

        private static void MigratePositionFiles(string sourcePath, string destinationPath)
        {
            string migrationMarker = destinationPath + ".migrated";
            if (File.Exists(migrationMarker))
                return;

            try
            {
                bool foundSource = false;
                foreach (string suffix in new[] { "", ".order", ".activity", ".activity.manual" })
                {
                    string source = sourcePath + suffix;
                    string destination = destinationPath + suffix;
                    if (!File.Exists(source))
                        continue;

                    foundSource = true;
                    // Preserve the original for retries and older app versions. Never overwrite
                    // a position the user has already saved under the new monitor identity.
                    if (!File.Exists(destination))
                        File.Copy(source, destination);
                }

                // A later user reset may delete a position or manual-position marker. Do not
                // restore those deliberately removed files the next time the host is created.
                if (foundSource)
                    File.WriteAllText(migrationMarker, "1");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Could not migrate taskbar layout {Path.GetFileName(sourcePath)}");
            }
        }
    }
}
