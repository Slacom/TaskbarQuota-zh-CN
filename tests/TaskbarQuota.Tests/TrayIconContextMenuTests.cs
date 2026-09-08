using H.NotifyIcon.Core;
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
}
