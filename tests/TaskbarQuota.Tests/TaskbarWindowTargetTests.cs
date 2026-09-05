using System.Linq;
using TaskbarQuota.Interop;
using TaskbarQuota.Taskbar;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

public class TaskbarWindowTargetTests
{
    private const string MonitorA = @"\\?\DISPLAY#GENERIC#5&abc&0&UID100#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string MonitorB = @"\\?\DISPLAY#GENERIC#5&abc&0&UID101#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    [Theory]
    [InlineData(TaskbarWindowTarget.PrimaryClassName, true)]
    [InlineData(TaskbarWindowTarget.SecondaryClassName, false)]
    public void IsTaskbarClassName_AcceptsWindowsTaskbarClasses(string className, bool expectedPrimary)
    {
        Assert.True(TaskbarWindowTarget.IsTaskbarClassName(className, out bool isPrimary));
        Assert.Equal(expectedPrimary, isPrimary);
    }

    [Fact]
    public void IsTaskbarClassName_RejectsOtherShellWindows()
    {
        Assert.False(TaskbarWindowTarget.IsTaskbarClassName("WorkerW", out bool isPrimary));
        Assert.False(isPrimary);
    }

    [Fact]
    public void BuildDisplayKey_UsesStableSanitizedDisplayId()
    {
        var displayKey = TaskbarWindowTarget.BuildDisplayKey(
            @"\\.\DISPLAY2",
            new RECT { left = 2560, top = 0, right = 4480, bottom = 1080 });

        Assert.Equal("DISPLAY2", displayKey);
        Assert.Equal("taskbar-widget-position-DISPLAY2.txt", TaskbarWindowTarget.BuildPositionFileName(displayKey));
    }

    [Fact]
    public void BuildDisplayKey_uses_case_insensitive_monitor_interface_independent_of_gdi_name()
    {
        var displayKey = TaskbarWindowTarget.BuildDisplayKey(
            @"\\.\DISPLAY7",
            MonitorA,
            new RECT { left = 2560, top = 0, right = 4480, bottom = 1080 });

        Assert.StartsWith("MONITOR_", displayKey);
        Assert.Equal(72, displayKey.Length);
        Assert.Equal(displayKey, TaskbarWindowTarget.BuildDisplayKey("DISPLAY2", MonitorA.ToLowerInvariant(), default));
        Assert.NotEqual(displayKey, TaskbarWindowTarget.BuildDisplayKey("DISPLAY7", MonitorB, default));
        Assert.NotEqual(
            TaskbarWindowTarget.BuildDisplayKey("DISPLAY7", "a#bc", default),
            TaskbarWindowTarget.BuildDisplayKey("DISPLAY7", "ab#c", default));
    }

    [Fact]
    public void BuildDisplayKey_FallsBackToBoundsWhenDisplayIdIsUnavailable()
    {
        var displayKey = TaskbarWindowTarget.BuildDisplayKey(
            null,
            new RECT { left = -1920, top = 0, right = 0, bottom = 1080 });

        Assert.Equal("-1920_0_0_1080", displayKey);
        Assert.Equal("taskbar-widget-position--1920_0_0_1080.txt", TaskbarWindowTarget.BuildPositionFileName(displayKey));
    }

    [Theory]
    [InlineData("DISPLAY1", 1)]
    [InlineData("display12", 12)]
    [InlineData("-1920_0_0_1080", 0)]
    [InlineData("", 0)]
    public void TryGetDisplayNumber_reads_gdi_display_key(string displayKey, int expected)
        => Assert.Equal(expected, TaskbarWindowTarget.TryGetDisplayNumber(displayKey));

    [Theory]
    [InlineData("DISPLAY2", "Screen 2")]
    [InlineData("legacy-monitor-key", "this screen")]
    [InlineData(WidgetSettingsService.AllDisplaysPinDestination, "all screens")]
    public void GetDisplayLabel_never_exposes_internal_display_keys(string displayKey, string expected)
        => Assert.Equal(expected, TaskbarWindowTarget.GetDisplayLabel(displayKey));

    [Fact]
    public void FormatScreenLabel_uses_current_layout_ordinals()
    {
        Assert.Equal("Screen 1 (primary)", TaskbarWindowTarget.FormatScreenLabel(1, isPrimary: true));
        Assert.Equal("Screen 2", TaskbarWindowTarget.FormatScreenLabel(2, isPrimary: false));
        Assert.Equal("Screen 1", TaskbarWindowTarget.FormatScreenLabel(1, isPrimary: true, includePrimarySuffix: false));
    }

    [Fact]
    public void OrderForDisplay_puts_primary_first_then_left_to_right()
    {
        var rightSecondary = Target(
            handle: 3,
            primary: false,
            key: "RIGHT",
            gdi: "DISPLAY7",
            monitor: 30,
            left: 2560);
        var leftSecondary = Target(
            handle: 2,
            primary: false,
            key: "LEFT",
            gdi: "DISPLAY6",
            monitor: 20,
            left: -1920);
        var primary = Target(
            handle: 1,
            primary: true,
            key: "PRIMARY",
            gdi: "DISPLAY1",
            monitor: 10,
            left: 0);

        var ordered = TaskbarWindowTarget.OrderForDisplay([rightSecondary, leftSecondary, primary]);

        Assert.Equal(["PRIMARY", "LEFT", "RIGHT"], ordered.Select(target => target.DisplayKey));
        Assert.Equal(
            "Screen 2",
            TaskbarWindowTarget.GetDisplayLabel("LEFT", ordered));
    }

    [Fact]
    public void SelectCanonicalTaskbars_keeps_one_window_per_monitor()
    {
        var live = Target(1, true, "LIVE", "DISPLAY7", monitor: 10, left: 2560, width: 1920);
        var stale = Target(2, false, "STALE", "DISPLAY2", monitor: 10, left: 2560, width: 800);

        var chosen = TaskbarWindowTarget.SelectCanonicalTaskbars([stale, live]);

        Assert.Single(chosen);
        Assert.Equal("LIVE", chosen[0].DisplayKey);
        Assert.Equal(new IntPtr(1), chosen[0].Handle);
    }

    [Fact]
    public void SelectCanonicalTaskbars_drops_windows_without_a_monitor()
    {
        var orphan = Target(4, false, "ORPHAN", "DISPLAY9", monitor: 0, left: 0);
        var live = Target(5, true, "PRIMARY", "DISPLAY1", monitor: 11, left: 0);

        var chosen = TaskbarWindowTarget.SelectCanonicalTaskbars([orphan, live]);

        Assert.Equal(["PRIMARY"], chosen.Select(target => target.DisplayKey));
    }

    [Fact]
    public void Canonical_targets_use_distinct_monitor_instances_and_drop_ambiguous_legacy_aliases()
    {
        var left = InstanceTarget(1, true, MonitorA, "DISPLAY1", 1, 0);
        var right = InstanceTarget(2, false, MonitorB, "DISPLAY2", 2, 1920);

        var unique = TaskbarWindowTarget.SelectCanonicalTaskbars([left, right]);

        Assert.NotEqual(unique[0].DisplayKey, unique[1].DisplayKey);
        Assert.All(unique, target => Assert.Empty(target.LegacyDisplayKey));
        var identities = unique.Select(target => target.ToIdentity()).ToArray();
        Assert.False(TaskbarWindowTarget.TryMigratePersistedKey("GENERIC", identities, out _));
        Assert.True(TaskbarWindowTarget.TryMigratePersistedKey("GENERIC_DISPLAY2", identities, out string migrated));
        Assert.Equal(right.DisplayKey, migrated);
    }

    [Fact]
    public void Selected_and_pinned_duplicate_monitor_survives_disconnect_reconnect_and_gdi_renumbering()
    {
        var primary = Target(3, true, "PRIMARY", "DISPLAY3", 30, -1920);
        var left = InstanceTarget(1, false, MonitorA, "DISPLAY1", 10, 0);
        var right = InstanceTarget(2, false, MonitorB, "DISPLAY2", 20, 1920);
        var initial = TaskbarWindowTarget.SelectCanonicalTaskbars([primary, left, right]);
        string saved = initial.Single(target => target.Monitor == left.Monitor).DisplayKey;

        IReadOnlyList<TaskbarWindowTarget>[] snapshots = [
            TaskbarWindowTarget.SelectCanonicalTaskbars([primary, left]),
            TaskbarWindowTarget.SelectCanonicalTaskbars([primary, right]),
            TaskbarWindowTarget.SelectCanonicalTaskbars([right, primary, left]),
            TaskbarWindowTarget.SelectCanonicalTaskbars([
                primary,
                InstanceTarget(11, false, MonitorA, "DISPLAY6", 100, 2560),
                InstanceTarget(12, false, MonitorB, "DISPLAY7", 200, -1920),
            ]),
        ];
        foreach (var targets in snapshots)
        {
            var identities = targets.Select(target => target.ToIdentity()).ToArray();
            Assert.Equal(saved, TaskbarWindowTarget.ResolvePersistedDisplayKey(saved, identities));
            Assert.False(TaskbarWindowTarget.TryMigratePersistedKey(saved, identities, out _));
            var available = targets.Select(target => target.DisplayKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var mode in new[] { TaskbarPlacementMode.SelectedDisplay, TaskbarPlacementMode.Adaptive })
            {
                foreach (var target in targets)
                {
                    bool routed = TaskbarContentRouter.IsRoutedToDisplay(
                        ProviderId.Codex, mode, saved, target.DisplayKey, primary.DisplayKey, available,
                        _ => null, _ => true, _ => saved);
                    Assert.Equal(target.DisplayKey == (available.Contains(saved) ? saved : primary.DisplayKey), routed);
                }
            }
        }
    }

    [Fact]
    public void Unambiguous_monitor_id_from_earlier_pr_build_migrates_to_interface_key()
    {
        var target = InstanceTarget(1, true, MonitorA, "DISPLAY6", 10, 0);
        Assert.True(TaskbarWindowTarget.TryMigratePersistedKey("GENERIC", [target.ToIdentity()], out string migrated));
        Assert.Equal(target.DisplayKey, migrated);
    }

    [Fact]
    public void ResolvePersistedDisplayKey_matches_current_gdi_name()
    {
        DisplayIdentity[] live =
        [
            new("MONITORA", "DISPLAY6", true),
            new("MONITORB", "DISPLAY7", false),
        ];

        Assert.Equal("MONITORB", TaskbarWindowTarget.ResolvePersistedDisplayKey("DISPLAY7", live));
        Assert.Equal("MONITORA", TaskbarWindowTarget.ResolvePersistedDisplayKey("MONITORA", live));
    }

    [Fact]
    public void ResolvePersistedDisplayKey_maps_orphaned_secondary_gdi_name_to_the_only_other_screen()
    {
        DisplayIdentity[] live =
        [
            new("MONITORA", "DISPLAY6", true),
            new("MONITORB", "DISPLAY7", false),
        ];

        Assert.Equal("MONITORB", TaskbarWindowTarget.ResolvePersistedDisplayKey("DISPLAY2", live));
        Assert.False(TaskbarWindowTarget.TryMigratePersistedKey("DISPLAY2", live, out string persisted));
        Assert.Equal("DISPLAY2", persisted);
    }

    [Fact]
    public void ResolvePersistedDisplayKey_maps_display1_to_the_current_primary()
    {
        DisplayIdentity[] live =
        [
            new("MONITORA", "DISPLAY6", true),
            new("MONITORB", "DISPLAY7", false),
        ];

        Assert.Equal("MONITORA", TaskbarWindowTarget.ResolvePersistedDisplayKey("DISPLAY1", live));
    }

    [Fact]
    public void ResolvePersistedDisplayKey_keeps_orphaned_key_when_several_secondaries_exist()
    {
        DisplayIdentity[] live =
        [
            new("MONITORA", "DISPLAY6", true),
            new("MONITORB", "DISPLAY7", false),
            new("MONITORC", "DISPLAY8", false),
        ];

        Assert.Equal("DISPLAY2", TaskbarWindowTarget.ResolvePersistedDisplayKey("DISPLAY2", live));
        Assert.False(TaskbarWindowTarget.TryMigratePersistedKey("DISPLAY2", live, out _));
    }

    [Fact]
    public void Window_monitor_uses_canonical_key_for_adaptive_history_with_duplicate_ids()
    {
        var targets = TaskbarWindowTarget.SelectCanonicalTaskbars([
            InstanceTarget(1, true, MonitorA, "DISPLAY1", 10, 0),
            InstanceTarget(2, false, MonitorB, "DISPLAY2", 20, 1920),
        ]);
        string windowKey = TaskbarWindowTarget.GetDisplayKeyForMonitor(new IntPtr(20), targets);
        var history = new AdaptiveDisplayProviderState();
        history.Observe(ProviderId.Codex, windowKey, new IntPtr(100));

        Assert.Equal(TaskbarWindowTarget.BuildDisplayKey("DISPLAY2", MonitorB, default), windowKey);
        Assert.Equal(ProviderId.Codex, history.GetProvider(targets[1].DisplayKey));
        Assert.Null(history.GetProvider(targets[0].DisplayKey));
        Assert.Equal(string.Empty, TaskbarWindowTarget.GetDisplayKeyForMonitor(IntPtr.Zero, targets));
        Assert.Equal(string.Empty, TaskbarWindowTarget.GetDisplayKeyForMonitor(new IntPtr(99), targets));
    }

    [Fact]
    public void Migration_preserves_selection_until_missing_monitor_returns()
    {
        DisplayIdentity[] partial = [
            new("MONITORA", "DISPLAY6", true),
            new("MONITORB", "DISPLAY7", false),
        ];
        // Even repeated incomplete scans must not permanently assign DISPLAY8 to B.
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal("MONITORB", TaskbarWindowTarget.ResolvePersistedDisplayKey("DISPLAY8", partial));
            Assert.False(TaskbarWindowTarget.TryMigratePersistedKey("DISPLAY8", partial, out string saved));
            Assert.Equal("DISPLAY8", saved);
        }

        DisplayIdentity[] complete = [.. partial, new("MONITORC", "DISPLAY8", false)];
        Assert.True(TaskbarWindowTarget.TryMigratePersistedKey("DISPLAY8", complete, out string migrated));
        Assert.Equal("MONITORC", migrated);
        Assert.False(TaskbarWindowTarget.TryMigratePersistedKey(migrated, complete, out _));
    }

    [Fact]
    public void Migration_does_not_persist_a_guessed_primary()
    {
        DisplayIdentity[] live = [new("MONITORA", "DISPLAY6", true)];
        Assert.Equal("MONITORA", TaskbarWindowTarget.ResolvePersistedDisplayKey("DISPLAY1", live));
        Assert.False(TaskbarWindowTarget.TryMigratePersistedKey("DISPLAY1", live, out string saved));
        Assert.Equal("DISPLAY1", saved);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Position_migration_preserves_layout_files_and_existing_destination(bool primary, bool legacy)
    {
        string directory = Path.Combine(Path.GetTempPath(), "taskbarquota-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Target(1, primary, "MONITORB", "DISPLAY2", 20, 1920);
            string source = Path.Combine(directory, legacy
                ? "taskbar-widget-position.txt"
                : "taskbar-widget-position-DISPLAY2.txt");
            string destination = Path.Combine(directory, "taskbar-widget-position-MONITORB.txt");
            string[] suffixes = ["", ".order", ".activity", ".activity.manual"];
            string[] contents = ["123", "ActivityFirst", "456", "1"];
            for (int i = 0; i < suffixes.Length; i++)
                File.WriteAllText(source + suffixes[i], contents[i]);

            Assert.Equal(destination, target.GetPositionPath(directory));
            for (int i = 0; i < suffixes.Length; i++)
            {
                Assert.Equal(contents[i], File.ReadAllText(destination + suffixes[i]));
                Assert.Equal(contents[i], File.ReadAllText(source + suffixes[i]));
                File.WriteAllText(destination + suffixes[i], "updated");
            }

            target.GetPositionPath(directory);
            foreach (string suffix in suffixes)
                Assert.Equal("updated", File.ReadAllText(destination + suffix));

            // Clearing manual layout must survive another host creation, even with old files present.
            foreach (string suffix in suffixes)
                File.Delete(destination + suffix);
            target.GetPositionPath(directory);
            foreach (string suffix in suffixes)
                Assert.False(File.Exists(destination + suffix));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Position_migration_copies_sidecars_even_when_main_position_already_exists()
    {
        string directory = Path.Combine(Path.GetTempPath(), "taskbarquota-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Target(1, false, "MONITORB", "DISPLAY2", 20, 1920);
            string source = Path.Combine(directory, "taskbar-widget-position-DISPLAY2.txt");
            string destination = Path.Combine(directory, "taskbar-widget-position-MONITORB.txt");
            File.WriteAllText(destination, "789");
            File.WriteAllText(source + ".activity", "456");
            File.WriteAllText(source + ".activity.manual", "1");

            target.GetPositionPath(directory);

            Assert.Equal("789", File.ReadAllText(destination));
            Assert.Equal("456", File.ReadAllText(destination + ".activity"));
            Assert.Equal("1", File.ReadAllText(destination + ".activity.manual"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("GENERIC")]
    [InlineData("GENERIC_DISPLAY2")]
    public void Interface_key_migration_preserves_earlier_pr_layout_and_subsequent_reset(string oldKey)
    {
        string directory = Path.Combine(Path.GetTempPath(), "taskbarquota-interface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = InstanceTarget(2, false, MonitorB, "DISPLAY2", 20, 1920);
            string oldPath = Path.Combine(directory, TaskbarWindowTarget.BuildPositionFileName(oldKey));
            string gdiPath = Path.Combine(directory, TaskbarWindowTarget.BuildPositionFileName("DISPLAY2"));
            File.WriteAllText(oldPath, "456");
            File.WriteAllText(oldPath + ".migrated", "1");
            File.WriteAllText(gdiPath, "123");

            string currentPath = target.GetPositionPath(directory);
            Assert.Equal("456", File.ReadAllText(currentPath));
            Assert.Equal(currentPath, (target with { GdiDeviceName = "DISPLAY7" }).GetPositionPath(directory));

            // Simulate a layout reset before installing this revision: only the old migration receipt remains.
            File.Delete(oldPath);
            File.Delete(currentPath);
            File.Delete(currentPath + ".migrated");
            target.GetPositionPath(directory);
            Assert.False(File.Exists(currentPath));
            Assert.True(File.Exists(currentPath + ".migrated"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TaskbarWindowTarget InstanceTarget(
        int handle, bool primary, string instance, string gdi, int monitor, int left)
        => Target(handle, primary, TaskbarWindowTarget.BuildDisplayKey(gdi, instance, default), gdi, monitor, left)
            with { LegacyDisplayKey = "GENERIC", LegacySuffixedDisplayKey = $"GENERIC_{gdi}" };

    private static TaskbarWindowTarget Target(
        int handle,
        bool primary,
        string key,
        string gdi,
        int monitor,
        int left,
        int width = 1920)
        => new(
            new IntPtr(handle),
            primary,
            key,
            gdi,
            new IntPtr(monitor),
            new RECT { left = left, top = 0, right = left + width, bottom = 1080 });
}
