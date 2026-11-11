using TaskbarQuota.Services;

namespace TaskbarQuota.Tests;

[Collection(WidgetRowSettingsCollection.Name)]
public class WidgetSurfaceModeTests
{
    /// <summary>
    /// WidgetSettingsService is process-global. Capture and restore surface/opacity so these tests
    /// cannot leak into others (or observe another test's mutations under parallel xUnit).
    /// </summary>
    private static void WithIsolatedSurfaceSettings(Action<string> body)
    {
        var previousSurface = WidgetSettingsService.CurrentSurface;
        var previousOpacity = WidgetSettingsService.FloatingOpacity;
        var previousWindowOpacity = WidgetSettingsService.FloatingWindowOpacity;
        var previousClickThrough = WidgetSettingsService.FloatingClickThrough;
        var directory = Path.Combine(Path.GetTempPath(), "taskbarquota-surface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var storageOverride = AppStorage.OverrideAppDataDirectoryForTesting(directory);
            WidgetSettingsService.ReloadSurfaceSettingsForTesting();
            body(directory);
        }
        finally
        {
            WidgetSettingsService.RestoreSurfaceSettingsForTesting(
                previousSurface, previousOpacity, previousWindowOpacity, previousClickThrough);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Default_surface_is_taskbar()
    {
        WithIsolatedSurfaceSettings(directory =>
        {
            Assert.Equal(WidgetSurfaceMode.Taskbar, WidgetSettingsService.CurrentSurface);
        });
    }

    [Fact]
    public void ApplySurface_switches_to_floating_and_back()
    {
        WithIsolatedSurfaceSettings(directory =>
        {
            WidgetSettingsService.ApplySurface(WidgetSurfaceMode.Taskbar);
            Assert.Equal(WidgetSurfaceMode.Taskbar, WidgetSettingsService.CurrentSurface);

            WidgetSettingsService.ApplySurface(WidgetSurfaceMode.Floating);
            Assert.Equal(WidgetSurfaceMode.Floating, WidgetSettingsService.CurrentSurface);
            Assert.Equal(int.MaxValue, PinBudgetService.AvailableLogicalWidth);
            Assert.Equal("1", File.ReadAllText(Path.Combine(directory, "widget-surface-mode.txt")));

            WidgetSettingsService.ReloadSurfaceSettingsForTesting();
            Assert.Equal(WidgetSurfaceMode.Floating, WidgetSettingsService.CurrentSurface);

            WidgetSettingsService.ApplySurface(WidgetSurfaceMode.Taskbar);
            Assert.Equal(WidgetSurfaceMode.Taskbar, WidgetSettingsService.CurrentSurface);
        });
    }

    [Fact]
    public void ApplySurface_same_value_is_noop()
    {
        WithIsolatedSurfaceSettings(_ =>
        {
            WidgetSettingsService.ApplySurface(WidgetSurfaceMode.Taskbar);
            int changed = 0;
            void Handler(object? s, EventArgs e) => changed++;
            WidgetSettingsService.Changed += Handler;
            try
            {
                WidgetSettingsService.ApplySurface(WidgetSurfaceMode.Taskbar);
                Assert.Equal(0, changed);
            }
            finally
            {
                WidgetSettingsService.Changed -= Handler;
            }
        });
    }

    [Fact]
    public void ApplyFloatingOpacity_clamps_and_round_trips()
    {
        WithIsolatedSurfaceSettings(directory =>
        {
            WidgetSettingsService.ApplyFloatingOpacity(0.5);
            Assert.Equal(0.5, WidgetSettingsService.FloatingOpacity, precision: 2);
            Assert.Equal("50", File.ReadAllText(Path.Combine(directory, "floating-opacity.txt")));

            WidgetSettingsService.ReloadSurfaceSettingsForTesting();
            Assert.Equal(0.5, WidgetSettingsService.FloatingOpacity, precision: 2);

            WidgetSettingsService.ApplyFloatingOpacity(0.1); // below min
            Assert.Equal(WidgetSettingsService.FloatingOpacityMin, WidgetSettingsService.FloatingOpacity, precision: 2);

            WidgetSettingsService.ApplyFloatingOpacity(1.5); // above max
            Assert.Equal(WidgetSettingsService.FloatingOpacityMax, WidgetSettingsService.FloatingOpacity, precision: 2);

            WidgetSettingsService.ApplyFloatingOpacity(WidgetSettingsService.FloatingOpacityDefault);
        });
    }

    [Fact]
    public void StepOpacityFromWheel_steps_by_five_percent_and_clamps()
    {
        Assert.Equal(0.55, FloatingUsageWindow.StepOpacityFromWheel(0.50, +120), precision: 2);
        Assert.Equal(0.45, FloatingUsageWindow.StepOpacityFromWheel(0.50, -120), precision: 2);
        Assert.Equal(WidgetSettingsService.FloatingOpacityMax,
            FloatingUsageWindow.StepOpacityFromWheel(0.98, +120), precision: 2);
        Assert.Equal(WidgetSettingsService.FloatingOpacityMin,
            FloatingUsageWindow.StepOpacityFromWheel(0.36, -120), precision: 2);
        // Sub-notch deltas still count as one step (precision touchpad).
        Assert.Equal(0.55, FloatingUsageWindow.StepOpacityFromWheel(0.50, +40), precision: 2);
    }

    [Fact]
    public void StepWindowOpacityFromWheel_uses_window_opacity_range()
    {
        Assert.Equal(0.55, FloatingUsageWindow.StepWindowOpacityFromWheel(0.50, +120), precision: 2);
        Assert.Equal(0.45, FloatingUsageWindow.StepWindowOpacityFromWheel(0.50, -120), precision: 2);
        // Can go below the 35% Acrylic floor, down to the 20% window-opacity floor.
        Assert.Equal(0.25, FloatingUsageWindow.StepWindowOpacityFromWheel(0.30, -120), precision: 2);
        Assert.Equal(WidgetSettingsService.FloatingWindowOpacityMin,
            FloatingUsageWindow.StepWindowOpacityFromWheel(0.22, -240), precision: 2);
        Assert.Equal(WidgetSettingsService.FloatingWindowOpacityMax,
            FloatingUsageWindow.StepWindowOpacityFromWheel(0.98, +120), precision: 2);
    }

    [Theory]
    [InlineData(false, false, 120, false, true)]
    [InlineData(false, false, -120, false, true)]
    [InlineData(false, false, 120, true, false)]
    [InlineData(true, false, 120, false, false)]
    [InlineData(false, true, 120, false, false)]
    [InlineData(false, false, 0, false, false)]
    public void OpacityWheel_is_exclusive_with_agent_navigation(
        bool isDragging,
        bool isHorizontalWheel,
        int wheelDelta,
        bool overAgentActivity,
        bool expected)
    {
        Assert.Equal(expected, FloatingUsageWindow.ShouldAdjustOpacityFromWheel(
            isDragging, isHorizontalWheel, wheelDelta, overAgentActivity));
    }

    [Fact]
    public void ComputeAcrylicMaterial_clamps_and_preserves_native_material_at_maximum()
    {
        var minimum = FloatingUsageWindow.ComputeAcrylicMaterial(0);
        var middle = FloatingUsageWindow.ComputeAcrylicMaterial(0.675);
        var maximum = FloatingUsageWindow.ComputeAcrylicMaterial(2);

        Assert.Equal(0.25f, minimum.TintOpacity, precision: 3);
        Assert.Equal(0.45f, minimum.LuminosityOpacity, precision: 3);
        Assert.InRange(middle.TintOpacity, minimum.TintOpacity, maximum.TintOpacity);
        Assert.InRange(middle.LuminosityOpacity, minimum.LuminosityOpacity, maximum.LuminosityOpacity);
        Assert.Equal(0.80f, maximum.TintOpacity, precision: 3);
        Assert.Equal(0.80f, maximum.LuminosityOpacity, precision: 3);
        Assert.True(maximum.TintOpacity < 1, "Maximum strength must still reveal the native Acrylic material.");
    }

    [Fact]
    public void Floating_window_opacity_and_click_through_default_to_current_behavior()
    {
        WithIsolatedSurfaceSettings(_ =>
        {
            Assert.Equal(1.0, WidgetSettingsService.FloatingWindowOpacity, precision: 2);
            Assert.False(WidgetSettingsService.FloatingClickThrough);
        });
    }

    [Fact]
    public void ApplyFloatingWindowOpacity_clamps_and_round_trips()
    {
        WithIsolatedSurfaceSettings(directory =>
        {
            WidgetSettingsService.ApplyFloatingWindowOpacity(0.25);
            Assert.Equal(0.25, WidgetSettingsService.FloatingWindowOpacity, precision: 2);
            Assert.Equal("25", File.ReadAllText(Path.Combine(directory, "floating-window-opacity.txt")));

            WidgetSettingsService.ReloadSurfaceSettingsForTesting();
            Assert.Equal(0.25, WidgetSettingsService.FloatingWindowOpacity, precision: 2);

            WidgetSettingsService.ApplyFloatingWindowOpacity(0.01);
            Assert.Equal(WidgetSettingsService.FloatingWindowOpacityMin, WidgetSettingsService.FloatingWindowOpacity, precision: 2);

            WidgetSettingsService.ApplyFloatingWindowOpacity(1.5);
            Assert.Equal(WidgetSettingsService.FloatingWindowOpacityMax, WidgetSettingsService.FloatingWindowOpacity, precision: 2);
        });
    }

    [Fact]
    public void ApplyFloatingClickThrough_persists_and_raises_changed_once()
    {
        WithIsolatedSurfaceSettings(directory =>
        {
            int changed = 0;
            void Handler(object? s, EventArgs e) => changed++;
            WidgetSettingsService.Changed += Handler;
            try
            {
                WidgetSettingsService.ApplyFloatingClickThrough(true);
                WidgetSettingsService.ApplyFloatingClickThrough(true);
                Assert.Equal(1, changed);
            }
            finally
            {
                WidgetSettingsService.Changed -= Handler;
            }

            Assert.Equal("1", File.ReadAllText(Path.Combine(directory, "floating-click-through.txt")));
            WidgetSettingsService.ReloadSurfaceSettingsForTesting();
            Assert.True(WidgetSettingsService.FloatingClickThrough);
        });
    }

    private const long Layered = 0x80000;
    private const long Transparent = 0x20;
    private const long Unrelated = 0x08000000; // WS_EX_NOACTIVATE must survive every combination.

    [Theory]
    [InlineData(Unrelated, false, false, Unrelated)]
    [InlineData(Unrelated | Layered | Transparent, false, false, Unrelated)]
    [InlineData(Unrelated, true, false, Unrelated | Layered)]
    [InlineData(Unrelated, true, true, Unrelated | Layered | Transparent)]
    [InlineData(Unrelated | Layered | Transparent, true, false, Unrelated | Layered)]
    // Click-through without layering would not pass input through, so it forces WS_EX_LAYERED.
    [InlineData(Unrelated, false, true, Unrelated | Layered | Transparent)]
    public void ComputeExStyle_toggles_only_layered_and_transparent_bits(
        long current, bool layered, bool transparent, long expected)
    {
        Assert.Equal(expected, FloatingUsageWindow.ComputeExStyle(current, layered, transparent));
    }

    [Theory]
    [InlineData(1.0, 255)]
    [InlineData(0.5, 128)]
    [InlineData(0.25, 64)]
    [InlineData(0.0, 51)] // clamped to the 20% floor
    [InlineData(2.0, 255)]
    public void ComputeLayeredAlpha_scales_and_clamps(double opacity, int expected)
    {
        Assert.Equal((byte)expected, FloatingUsageWindow.ComputeLayeredAlpha(opacity));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)] // Ctrl held elsewhere must not grab clicks meant for other apps
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]  // releasing Ctrl mid-drag keeps the drag alive
    [InlineData(false, true, true, true)]
    public void ShouldAllowInteraction_requires_ctrl_over_window_or_active_drag(
        bool modifierHeld, bool cursorOverWindow, bool pointerActive, bool expected)
    {
        Assert.Equal(expected, FloatingUsageWindow.ShouldAllowInteraction(modifierHeld, cursorOverWindow, pointerActive));
    }
}
