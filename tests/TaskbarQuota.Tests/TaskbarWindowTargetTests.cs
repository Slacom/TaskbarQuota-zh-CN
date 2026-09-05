using System.Linq;
using TaskbarQuota.Interop;
using TaskbarQuota.Taskbar;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

public class TaskbarWindowTargetTests
{
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
    public void BuildDisplayKey_PrefersStableMonitorIdOverGdiName()
    {
        var displayKey = TaskbarWindowTarget.BuildDisplayKey(
            @"\\.\DISPLAY7",
            @"MONITOR\DELA0D5\{4d36e96e-e325-11ce-bfc1-08002be10318}\0004",
            new RECT { left = 2560, top = 0, right = 4480, bottom = 1080 });

        Assert.Equal("MONITORDELA0D54d36e96e-e325-11ce-bfc1-08002be103180004", displayKey);
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
    public void DisambiguateDisplayKeys_suffixes_identical_monitor_ids()
    {
        var left = Target(1, true, "GENERIC", "DISPLAY1", monitor: 1, left: 0);
        var right = Target(2, false, "GENERIC", "DISPLAY2", monitor: 2, left: 1920);

        var unique = TaskbarWindowTarget.DisambiguateDisplayKeys([left, right]);

        Assert.Equal(["GENERIC_DISPLAY1", "GENERIC_DISPLAY2"], unique.Select(target => target.DisplayKey));
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
            Target(1, true, "GENERIC", "DISPLAY1", 10, 0),
            Target(2, false, "GENERIC", "DISPLAY2", 20, 1920),
        ]);
        string windowKey = TaskbarWindowTarget.GetDisplayKeyForMonitor(new IntPtr(20), targets);
        var history = new AdaptiveDisplayProviderState();
        history.Observe(ProviderId.Codex, windowKey, new IntPtr(100));

        Assert.Equal("GENERIC_DISPLAY2", windowKey);
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
