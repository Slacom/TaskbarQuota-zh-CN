using System.Linq;
using TaskbarQuota.Interop;
using TaskbarQuota.Taskbar;

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
        Assert.True(TaskbarWindowTarget.TryMigratePersistedKey("DISPLAY2", live, out string migrated));
        Assert.Equal("MONITORB", migrated);
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
