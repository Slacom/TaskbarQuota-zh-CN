using System;
using System.Diagnostics;
using System.Linq;
using TaskbarQuota.ActiveApp;

namespace TaskbarQuota.Tests;

public class ProcessSnapshotTests
{
    [Fact]
    public void Capture_ListsCurrentProcessWithItsExecutableName()
    {
        using var self = Process.GetCurrentProcess();

        var entry = ProcessSnapshot.Capture().Single(e => e.Pid == self.Id);

        Assert.Equal(self.ProcessName + ".exe", entry.ExeName, ignoreCase: true);
    }

    [Fact]
    public void Capture_ReportsTheRealParentProcess()
    {
        using var self = Process.GetCurrentProcess();
        var snapshot = ProcessSnapshot.Capture();

        var entry = snapshot.Single(e => e.Pid == self.Id);

        Assert.True(entry.ParentPid > 0);
    }

    [Fact]
    public void TryGetProcessName_MatchesManagedProcessName()
    {
        using var self = Process.GetCurrentProcess();

        Assert.Equal(self.ProcessName, ProcessSnapshot.TryGetProcessName(self.Id), ignoreCase: true);
    }

    [Fact]
    public void TryGetProcessName_ReturnsNullForUnknownPid()
        => Assert.Null(ProcessSnapshot.TryGetProcessName(int.MaxValue - 1));

    [Fact]
    public void GetStartTime_MatchesManagedStartTime()
    {
        using var self = Process.GetCurrentProcess();
        var entry = ProcessSnapshot.Capture().Single(e => e.Pid == self.Id);

        var started = ProcessSnapshot.GetStartTime(entry.Pid, entry.ParentPid);

        Assert.True(Math.Abs((started - self.StartTime).TotalSeconds) < 1);
    }
}
