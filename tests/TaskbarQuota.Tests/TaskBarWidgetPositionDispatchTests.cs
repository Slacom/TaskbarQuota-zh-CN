using System;
using System.Threading;
using System.Threading.Tasks;
using TaskbarQuota.Taskbar;

namespace TaskbarQuota.Tests;

public class TaskBarWidgetPositionDispatchTests
{
    [Fact]
    public async Task WorkerPositionUpdateRunsOnlyAfterDispatcherExecutesIt()
    {
        Action? queued = null;
        int calls = 0;
        Task update = TaskBarWidget.DispatchPositionUpdateAsync(
            false,
            action => { queued = action; return true; },
            () => calls++,
            CancellationToken.None);

        Assert.NotNull(queued);
        Assert.Equal(0, calls);
        Assert.False(update.IsCompleted);

        queued();
        await update;
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DispatcherPositionFailureReachesTheCaller()
    {
        Action? queued = null;
        Task update = TaskBarWidget.DispatchPositionUpdateAsync(
            false,
            action => { queued = action; return true; },
            () => throw new InvalidOperationException("position failed"),
            CancellationToken.None);

        queued!();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => update);
        Assert.Equal("position failed", exception.Message);
    }
}
