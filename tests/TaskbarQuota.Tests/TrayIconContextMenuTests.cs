using H.NotifyIcon.Core;
using System;
using TaskbarQuota.Taskbar;

namespace TaskbarQuota.Tests;

public class TrayIconContextMenuTests
{
    [Fact]
    public void OnlyRightMouseReleaseOpensTheTrayContextMenu()
    {
        Assert.True(TaskBarManager.IsTrayContextMenuEvent(MouseEvent.IconRightMouseUp));
        Assert.False(TaskBarManager.IsTrayContextMenuEvent(MouseEvent.IconLeftMouseUp));
        Assert.False(TaskBarManager.IsTrayContextMenuEvent(MouseEvent.IconRightMouseDown));
    }

    [Fact]
    public void TrayIconHandleIsAssignedBeforeNativeCreate()
    {
        var calls = new List<string>();
        var handle = new IntPtr(1234);

        TaskBarManager.ConfigureTrayIconBeforeCreate(
            handle,
            assignedHandle =>
            {
                Assert.Equal(handle, assignedHandle);
                calls.Add("icon");
            },
            () => calls.Add("create"));

        Assert.Equal(["icon", "create"], calls);
    }

    [Fact]
    public void TrayIconCreateStillRunsWhenIconCouldNotBeLoaded()
    {
        var calls = new List<string>();

        TaskBarManager.ConfigureTrayIconBeforeCreate(
            IntPtr.Zero,
            _ => calls.Add("icon"),
            () => calls.Add("create"));

        Assert.Equal(["create"], calls);
    }
}
