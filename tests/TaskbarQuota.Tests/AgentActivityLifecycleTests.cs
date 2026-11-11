using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaskbarQuota;
using TaskbarQuota.AgentActivity;
using TaskbarQuota.Usage;
using Xunit;

namespace TaskbarQuota.Tests;

public sealed class AgentActivityLifecycleTests
{
    [Fact]
    public void SuccessfulScan_RemovesScannerItemsMissingFromNextSnapshot()
    {
        var service = new AgentActivityService();
        service.ApplyScan([Item("live", AgentActivityStatus.Working)]);

        service.ApplyScan([]);

        Assert.Empty(service.Snapshot.Items);
        Assert.False(service.Snapshot.HasLiveItems);
    }

    [Fact]
    public void SuccessfulScan_DoesNotRetainExpiredCompletion()
    {
        var service = new AgentActivityService();
        var old = Item("old", AgentActivityStatus.Completed) with
        {
            UpdatedAt = DateTimeOffset.Now - AgentActivityService.CompletedRetention - TimeSpan.FromSeconds(1),
        };

        service.ApplyScan([old]);

        Assert.Empty(service.Snapshot.Items);
    }

    [Fact]
    public void AcknowledgedCompletion_RemainsHiddenWhenScannerReturnsItAgain()
    {
        var service = new AgentActivityService();
        var completed = Item("done", AgentActivityStatus.Completed);
        service.ApplyScan([completed]);
        service.Acknowledge("done");

        service.ApplyScan([completed]);

        Assert.Empty(service.Snapshot.Items);
    }

    [Fact]
    public void DismissedIdleSession_StaysHiddenOnLaterScans()
    {
        var service = new AgentActivityService();
        var idle = Item("idle", AgentActivityStatus.Idle);
        service.ApplyScan([idle]);
        service.Dismiss("idle");

        service.ApplyScan([idle]);

        Assert.Empty(service.Snapshot.Items);
        Assert.Empty(service.Snapshot.TrackedItems);
        Assert.False(service.Snapshot.HasLiveItems);
    }

    [Fact]
    public void DismissedSession_ReappearsWhenUpdatedAtAdvances()
    {
        var service = new AgentActivityService();
        var idle = Item("idle", AgentActivityStatus.Idle);
        service.ApplyScan([idle]);
        service.Dismiss("idle");

        service.ApplyScan([idle with { UpdatedAt = idle.UpdatedAt + TimeSpan.FromSeconds(5) }]);

        Assert.Equal("idle", Assert.Single(service.Snapshot.Items).Id);
    }

    [Theory]
    [InlineData(AgentActivityStatus.Working)]
    [InlineData(AgentActivityStatus.Waiting)]
    public void DismissedSession_ReappearsWhenItBecomesActive(AgentActivityStatus status)
    {
        var service = new AgentActivityService();
        var idle = Item("idle", AgentActivityStatus.Idle);
        service.ApplyScan([idle]);
        service.Dismiss("idle");

        service.ApplyScan([idle with { Status = status }]);

        var item = Assert.Single(service.Snapshot.Items);
        Assert.Equal(status, item.Status);
        Assert.Contains(service.Snapshot.TrackedItems, tracked => tracked.Id == "idle");
    }

    [Fact]
    public void DismissedCompletion_StaysHiddenAndOthersRemain()
    {
        var service = new AgentActivityService();
        var done = Item("done", AgentActivityStatus.Completed);
        var other = Item("other", AgentActivityStatus.Failed);
        service.ApplyScan([done, other]);
        service.Dismiss("done");

        service.ApplyScan([done, other]);

        Assert.Equal("other", Assert.Single(service.Snapshot.Items).Id);
    }

    [Fact]
    public void DismissedSession_IsForgottenOnceScannerDropsIt()
    {
        var service = new AgentActivityService();
        var idle = Item("idle", AgentActivityStatus.Idle);
        service.ApplyScan([idle]);
        service.Dismiss("idle");
        service.ApplyScan([]);

        service.ApplyScan([idle]);

        Assert.Equal("idle", Assert.Single(service.Snapshot.Items).Id);
    }

    [Fact]
    public void DismissUnknownId_DoesNotRaiseChanged()
    {
        var service = new AgentActivityService();
        var raised = 0;
        service.Changed += _ => raised++;

        service.Dismiss("missing");

        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData(AgentActivityStatus.Working)]
    [InlineData(AgentActivityStatus.Waiting)]
    public void DismissActiveSession_IsIgnored(AgentActivityStatus status)
    {
        var service = new AgentActivityService();
        service.ApplyScan([Item("live", status)]);
        var raised = 0;
        service.Changed += _ => raised++;

        service.Dismiss("live");

        Assert.Equal(0, raised);
        Assert.Equal("live", Assert.Single(service.Snapshot.Items).Id);
        Assert.Contains(service.Snapshot.TrackedItems, tracked => tracked.Id == "live");
    }

    [Fact]
    public void IdleSession_DisappearsAfterIdleRetention()
    {
        var service = new AgentActivityService();
        var stale = Item("stale", AgentActivityStatus.Idle) with
        {
            UpdatedAt = DateTimeOffset.Now - AgentActivityService.IdleRetention - TimeSpan.FromSeconds(1),
        };
        var recent = Item("recent", AgentActivityStatus.Idle) with
        {
            UpdatedAt = DateTimeOffset.Now - AgentActivityService.IdleRetention + TimeSpan.FromMinutes(1),
        };

        service.ApplyScan([stale, recent]);

        Assert.Equal("recent", Assert.Single(service.Snapshot.Items).Id);
        Assert.DoesNotContain(service.Snapshot.TrackedItems, item => item.Id == "stale");
    }

    [Fact]
    public void IdleSession_WithPendingToolCall_IsNotExpired()
    {
        var service = new AgentActivityService();
        var longTool = Item("tool", AgentActivityStatus.Idle) with
        {
            UpdatedAt = DateTimeOffset.Now - AgentActivityService.IdleRetention - TimeSpan.FromMinutes(5),
            ActionPending = true,
        };

        service.ApplyScan([longTool]);

        Assert.Equal("tool", Assert.Single(service.Snapshot.Items).Id);
    }

    [Fact]
    public void ExpiredIdleSession_ReappearsOnNewActivity()
    {
        var service = new AgentActivityService();
        var stale = Item("s", AgentActivityStatus.Idle) with
        {
            UpdatedAt = DateTimeOffset.Now - AgentActivityService.IdleRetention - TimeSpan.FromSeconds(1),
        };
        service.ApplyScan([stale]);
        Assert.Empty(service.Snapshot.Items);

        service.ApplyScan([stale with { Status = AgentActivityStatus.Working, UpdatedAt = DateTimeOffset.Now }]);

        Assert.Equal(AgentActivityStatus.Working, Assert.Single(service.Snapshot.Items).Status);
    }

    [Fact]
    public void SettledStatusSince_IsLastActivity()
    {
        var service = new AgentActivityService();
        var idle = Item("idle", AgentActivityStatus.Idle) with { UpdatedAt = DateTimeOffset.Now - TimeSpan.FromMinutes(7) };

        service.ApplyScan([idle]);

        Assert.Equal(idle.UpdatedAt, Assert.Single(service.Snapshot.Items).StatusSince);
    }

    [Fact]
    public void ActiveStatusSince_IsStableAcrossScansAndResetsOnTransition()
    {
        var service = new AgentActivityService();
        var working = Item("w", AgentActivityStatus.Working);
        service.ApplyScan([working]);
        var firstSince = Assert.Single(service.Snapshot.Items).StatusSince;
        Assert.NotNull(firstSince);

        service.ApplyScan([working with { UpdatedAt = working.UpdatedAt + TimeSpan.FromSeconds(10), Step = "next" }]);
        Assert.Equal(firstSince, Assert.Single(service.Snapshot.Items).StatusSince);

        service.ApplyScan([working with { Status = AgentActivityStatus.Waiting }]);
        Assert.True(Assert.Single(service.Snapshot.Items).StatusSince >= firstSince);
        Assert.Equal(AgentActivityStatus.Waiting, Assert.Single(service.Snapshot.Items).Status);
    }

    [Theory]
    [InlineData(20, "刚刚")]
    [InlineData(60, "1分")]
    [InlineData(47 * 60 + 59, "47分")]
    [InlineData(3600, "1小时")]
    [InlineData(3600 + 5 * 60, "1小时5分")]
    [InlineData(26 * 3600, "1天")]
    public void FormatElapsed_UsesCompactUnits(int seconds, string expected)
        => Assert.Equal(expected, AgentActivityItem.FormatElapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void ThrowingSubscriber_DoesNotBlockOtherSubscribers()
    {
        var service = new AgentActivityService();
        var observed = 0;
        service.Changed += _ => throw new InvalidOperationException("test");
        service.Changed += _ => observed++;

        service.ApplyScan([Item("live", AgentActivityStatus.Working)]);

        Assert.Equal(1, observed);
    }

    [Fact]
    public void ItemsForDisplay_MovesSelectedAgentToFront()
    {
        var snapshot = new AgentActivitySnapshot([
            Item("first", AgentActivityStatus.Working),
            Item("selected", AgentActivityStatus.Waiting),
            Item("last", AgentActivityStatus.Completed),
        ]);

        Assert.Equal(["selected", "first", "last"], snapshot.ItemsForDisplay("selected").Select(item => item.Id));
        Assert.Equal(["first", "selected", "last"], snapshot.ItemsForDisplay("missing").Select(item => item.Id));
    }

    [Fact]
    public void CandidateSelection_AppliesLimitPerProvider()
    {
        var now = DateTimeOffset.Now;
        var candidates = Enumerable.Range(0, 25)
            .Select(index => (ProviderId.Codex, $"codex-{index}", now.AddSeconds(-index)))
            .Concat(Enumerable.Range(0, 3)
                .Select(index => (ProviderId.Claude, $"claude-{index}", now.AddMinutes(-10).AddSeconds(-index))))
            .ToArray();

        var selected = AgentActivityScanner.SelectCandidates(candidates, maxPerProvider: 20);

        Assert.Equal(20, selected.Count(item => item.Provider == ProviderId.Codex));
        Assert.Equal(3, selected.Count(item => item.Provider == ProviderId.Claude));
    }

    [Theory]
    [InlineData("powershell", "powershell.exe -Command rg codex", null)]
    [InlineData("node", "node.exe C:\\tools\\unrelated.js --query cline", null)]
    [InlineData("codex", "codex.exe exec", ProviderId.Codex)]
    [InlineData("node", "node.exe C:\\npm\\node_modules\\@anthropic-ai\\claude-code\\cli.js", ProviderId.Claude)]
    public void TerminalDetection_RequiresExecutableOrKnownPackage(
        string executable, string commandLine, ProviderId? expected)
        => Assert.Equal(expected, AgentActivityScanner.DetectTerminalProviderForTesting(executable, commandLine));

    [Fact]
    public void ClineSession_UsesFilenameWhenSessionIdIsBlank()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "fallback-session.json");
            File.WriteAllText(path, "{\"session_id\":\"\",\"metadata\":{\"title\":\"Fallback\"}}");

            var item = AgentActivityScanner.ReadClineForTesting(path);

            Assert.Equal("cline:fallback-session", item?.Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CodexThreadIndex_SkipsMalformedAndWrongTypeRows()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "session_index.jsonl");
            File.WriteAllLines(path,
            [
                "{\"id\":42,\"thread_name\":false}",
                "{not-finished",
                "{\"id\":\"thread-1\",\"thread_name\":\"Useful name\"}",
            ]);
            var resolver = new CodexThreadNameResolver(path);

            Assert.Equal("Useful name", resolver.GetName("thread-1"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProviderSwitchDebounce_RequiresConsecutiveMatchingSamples()
    {
        ProviderId? pending = null;
        var samples = 0;

        Assert.False(UsageCoordinator.ShouldAcceptDetectedProvider(
            ref pending, ref samples, ProviderId.Claude, requiredSamples: 2));
        Assert.False(UsageCoordinator.ShouldAcceptDetectedProvider(
            ref pending, ref samples, ProviderId.Codex, requiredSamples: 2));
        Assert.True(UsageCoordinator.ShouldAcceptDetectedProvider(
            ref pending, ref samples, ProviderId.Codex, requiredSamples: 2));
        Assert.Null(pending);
        Assert.Equal(0, samples);
    }

    private static AgentActivityItem Item(string id, AgentActivityStatus status)
    {
        var now = DateTimeOffset.Now;
        return new AgentActivityItem(id, ProviderId.Codex, id, status.ToString(), status, now, now);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TaskbarQuotaTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
