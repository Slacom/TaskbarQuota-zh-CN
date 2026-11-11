using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaskbarQuota.Diagnostics;
using TaskbarQuota.Usage;

namespace TaskbarQuota.AgentActivity;

/// <summary>
/// Shared activity state for the taskbar summary and flyout. Provider adapters can report exact steps
/// through Report; the current presence bridge provides a useful fallback until transcript adapters exist.
/// </summary>
public sealed class AgentActivityService
{
    public static AgentActivityService Instance { get; } = new();

    internal static readonly TimeSpan CompletedRetention = TimeSpan.FromMinutes(10);
    // Idle sessions whose process stays open (desktop apps, parked terminals) would otherwise linger for
    // hours. They come back as soon as the session reports new activity.
    internal static readonly TimeSpan IdleRetention = TimeSpan.FromMinutes(15);
    private const int MaxRetainedItems = 100;

    private readonly object _gate = new();
    private readonly Dictionary<string, AgentActivityItem> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acknowledged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _scannedIds = new(StringComparer.Ordinal);
    // Rows the user dismissed from the flyout, keyed by the UpdatedAt they had at dismissal. Scanner rows
    // come back every tick, so a dismissal holds until the session reports newer or actively-working activity.
    private readonly Dictionary<string, DateTimeOffset> _dismissed = new(StringComparer.Ordinal);
    // Transcripts carry no transition timestamp for active states, so Working/Waiting date from when this
    // service first saw them. Settled states date from the item's last activity instead.
    private readonly Dictionary<string, (AgentActivityStatus Status, DateTimeOffset Since)> _activeSince =
        new(StringComparer.Ordinal);
    // A run is the set of threads that were alive together. Completed members stay here until the
    // remaining members finish, allowing the taskbar to advance through concurrent project agents.
    private readonly HashSet<string> _runIds = new(StringComparer.Ordinal);
    private readonly AgentActivityScanner _scanner = new();
    private int _refreshInProgress;
    private int _generation;

    public event Action<AgentActivitySnapshot>? Changed;

    public async Task RefreshFromTranscriptsAsync(CancellationToken cancellationToken = default)
    {
        // WMI/process inspection can occasionally outlive a tick; keep the newest completed scan rather
        // than queuing stale scans behind it.
        if (Interlocked.Exchange(ref _refreshInProgress, 1) != 0)
            return;
        var generation = Volatile.Read(ref _generation);

        try
        {
            IReadOnlyList<AgentActivityItem> scanned;
            try
            {
                scanned = await Task.Run(() => _scanner.Scan(cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[activity] transcript scan failed");
                return;
            }

            if (generation == Volatile.Read(ref _generation))
                ApplyScan(scanned);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    internal void ApplyScan(IReadOnlyList<AgentActivityItem> scanned)
    {
        AgentActivitySnapshot? snapshot = null;
        Action<AgentActivitySnapshot>? handlers = null;
        lock (_gate)
        {
            var before = BuildSnapshot();
            var nextScannedIds = scanned.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

            // Scanner-owned rows are a point-in-time view. If a transcript disappears, ages out, or can
            // no longer be correlated with a process, retaining its previous live state is incorrect.
            foreach (var missingId in _scannedIds.Where(id => !nextScannedIds.Contains(id)).ToArray())
            {
                _items.Remove(missingId);
                _acknowledged.Remove(missingId);
                _dismissed.Remove(missingId);
                _activeSince.Remove(missingId);
                _runIds.Remove(missingId);
            }

            _scannedIds.Clear();
            foreach (var id in nextScannedIds)
                _scannedIds.Add(id);

            foreach (var item in scanned)
                ReleaseDismissalOnNewActivity(item);

            var now = DateTimeOffset.Now;
            var idleCutoff = now - IdleRetention;
            bool trackedRunIsLive = _runIds.Any(id => _items.TryGetValue(id, out var existing) && existing.IsLive
                && !_dismissed.ContainsKey(id));
            var scannedLive = scanned
                .Where(item => item.IsLive && !_dismissed.ContainsKey(item.Id) && !IsExpiredIdle(item, idleCutoff))
                .Select(item => item.Id)
                .ToArray();
            if (scannedLive.Length > 0 && !trackedRunIsLive && _runIds.Count > 0)
                _runIds.Clear();
            foreach (var id in scannedLive)
                _runIds.Add(id);

            foreach (var key in _items.Keys.Where(key => key.StartsWith("presence:", StringComparison.Ordinal)).ToArray())
                if (scanned.Any(item => item.IsLive && _items[key].Provider == item.Provider))
                    RemoveItem(key);

            var completionCutoff = now - CompletedRetention;
            foreach (var item in scanned)
            {
                if (item.IsLive)
                    _acknowledged.Remove(item.Id);
                if (IsExpiredIdle(item, idleCutoff))
                    RemoveItem(item.Id);
                else if (item.IsLive || item.UpdatedAt >= completionCutoff)
                    _items[item.Id] = WithStatusSince(item, now);
                else
                    RemoveItem(item.Id);
            }

            Prune(completionCutoff, idleCutoff);
            var after = BuildSnapshot();
            if (!SnapshotsEqual(before, after))
            {
                snapshot = after;
                handlers = Changed;
            }
        }

        Publish(handlers, snapshot);
    }

    public AgentActivitySnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return BuildSnapshot();
        }
    }

    public void SyncPresence(ProviderId? provider, bool isPresent)
    {
        if (provider is not { } id)
            return;

        string key = $"presence:{id}";
        AgentActivitySnapshot snapshot;
        Action<AgentActivitySnapshot>? handlers;
        lock (_gate)
        {
            if (isPresent)
            {
                var now = DateTimeOffset.Now;
                if (_items.TryGetValue(key, out var existing) && existing.IsLive)
                {
                    _items[key] = WithStatusSince(existing with { UpdatedAt = now }, now);
                }
                else
                {
                    _items[key] = WithStatusSince(new AgentActivityItem(
                        key, id, DisplayName(id), "Working", AgentActivityStatus.Working, now, now), now);
                }
            }
            else if (_items.TryGetValue(key, out var previous) && previous.IsLive)
            {
                var now = DateTimeOffset.Now;
                _items[key] = WithStatusSince(previous with
                {
                    Step = "Finished",
                    Status = AgentActivityStatus.Completed,
                    UpdatedAt = now,
                }, now);
            }

            snapshot = BuildSnapshot();
            handlers = Changed;
        }
        Publish(handlers, snapshot);
    }

    public void Report(string id, ProviderId provider, string title, string step,
        AgentActivityStatus status, DateTimeOffset? startedAt = null, int subagentCount = 0, string? detail = null)
    {
        AgentActivitySnapshot snapshot;
        Action<AgentActivitySnapshot>? handlers;
        lock (_gate)
        {
            var now = DateTimeOffset.Now;
            _items[id] = WithStatusSince(new AgentActivityItem(
                id, provider, title, step, status, startedAt ?? now, now, subagentCount, detail), now);
            if (status is AgentActivityStatus.Working or AgentActivityStatus.Waiting)
                _acknowledged.Remove(id);
            ReleaseDismissalOnNewActivity(_items[id]);
            Prune(now - CompletedRetention, now - IdleRetention);
            snapshot = BuildSnapshot();
            handlers = Changed;
        }
        Publish(handlers, snapshot);
    }

    public void Acknowledge(string id)
    {
        AgentActivitySnapshot snapshot;
        Action<AgentActivitySnapshot>? handlers;
        lock (_gate)
        {
            _acknowledged.Add(id);
            _runIds.Remove(id);
            snapshot = BuildSnapshot();
            handlers = Changed;
        }
        Publish(handlers, snapshot);
    }

    /// <summary>
    /// Hides one row from the flyout and taskbar summary, including live (e.g. idle) sessions. The row stays
    /// hidden across scans until the session reports a newer UpdatedAt or a Working/Waiting status.
    /// </summary>
    public void Dismiss(string id)
    {
        AgentActivitySnapshot snapshot;
        Action<AgentActivitySnapshot>? handlers;
        lock (_gate)
        {
            // A stale card can still expose dismiss after its session resumed; never hide active work.
            if (!_items.TryGetValue(id, out var item)
                || item.Status is AgentActivityStatus.Working or AgentActivityStatus.Waiting)
                return;
            _dismissed[id] = item.UpdatedAt;
            _acknowledged.Add(id);
            _runIds.Remove(id);
            snapshot = BuildSnapshot();
            handlers = Changed;
        }
        Publish(handlers, snapshot);
    }

    public void AcknowledgeAll()
    {
        AgentActivitySnapshot snapshot;
        Action<AgentActivitySnapshot>? handlers;
        lock (_gate)
        {
            foreach (var item in _items.Values)
                if (!item.IsLive)
                    _acknowledged.Add(item.Id);
            _runIds.RemoveWhere(id => _acknowledged.Contains(id));
            snapshot = BuildSnapshot();
            handlers = Changed;
        }
        Publish(handlers, snapshot);
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _scanner.ClearCache();
        AgentActivitySnapshot snapshot;
        Action<AgentActivitySnapshot>? handlers;
        lock (_gate)
        {
            _items.Clear();
            _acknowledged.Clear();
            _dismissed.Clear();
            _activeSince.Clear();
            _scannedIds.Clear();
            _runIds.Clear();
            snapshot = BuildSnapshot();
            handlers = Changed;
        }
        Publish(handlers, snapshot);
    }

    private AgentActivitySnapshot BuildSnapshot()
    {
        var items = _items.Values
            .Where(item => !_dismissed.ContainsKey(item.Id))
            .Where(item => item.IsLive || !_acknowledged.Contains(item.Id))
            .OrderByDescending(item => item.IsLive)
            .ThenByDescending(item => item.UpdatedAt)
            .ToArray();
        var runItems = items
            .Where(item => _runIds.Contains(item.Id))
            .OrderBy(item => item.StartedAt)
            .ToArray();
        return new AgentActivitySnapshot(items, runItems);
    }

    private void Prune(DateTimeOffset completionCutoff, DateTimeOffset idleCutoff)
    {
        foreach (var id in _items.Values
                     .Where(item => (!item.IsLive && item.UpdatedAt < completionCutoff) || IsExpiredIdle(item, idleCutoff))
                     .Select(item => item.Id)
                     .ToArray())
            RemoveItem(id);

        foreach (var id in _items.Values
                     .OrderByDescending(item => item.IsLive)
                     .ThenByDescending(item => item.UpdatedAt)
                     .Skip(MaxRetainedItems)
                     .Select(item => item.Id)
                     .ToArray())
            RemoveItem(id);

        _acknowledged.RemoveWhere(id => !_items.ContainsKey(id) && !_scannedIds.Contains(id));
        foreach (var id in _dismissed.Keys.Where(id => !_items.ContainsKey(id) && !_scannedIds.Contains(id)).ToArray())
            _dismissed.Remove(id);
        _runIds.RemoveWhere(id => !_items.ContainsKey(id));
    }

    private static bool IsExpiredIdle(AgentActivityItem item, DateTimeOffset idleCutoff)
        => item.Status == AgentActivityStatus.Idle && !item.ActionPending && item.UpdatedAt < idleCutoff;

    private AgentActivityItem WithStatusSince(AgentActivityItem item, DateTimeOffset now)
    {
        if (item.Status is not (AgentActivityStatus.Working or AgentActivityStatus.Waiting))
        {
            _activeSince.Remove(item.Id);
            return item with { StatusSince = item.UpdatedAt };
        }

        if (!_activeSince.TryGetValue(item.Id, out var tracked) || tracked.Status != item.Status)
        {
            tracked = (item.Status, now);
            _activeSince[item.Id] = tracked;
        }
        return item with { StatusSince = tracked.Since };
    }

    private void ReleaseDismissalOnNewActivity(AgentActivityItem item)
    {
        if (!_dismissed.TryGetValue(item.Id, out var dismissedAt))
            return;
        if (item.UpdatedAt > dismissedAt || item.Status is AgentActivityStatus.Working or AgentActivityStatus.Waiting)
        {
            _dismissed.Remove(item.Id);
            _acknowledged.Remove(item.Id);
        }
    }

    private void RemoveItem(string id)
    {
        _items.Remove(id);
        _runIds.Remove(id);
        _activeSince.Remove(id);
    }

    private static bool SnapshotsEqual(AgentActivitySnapshot left, AgentActivitySnapshot right)
        => left.Items.SequenceEqual(right.Items)
            && left.TrackedItems.SequenceEqual(right.TrackedItems);

    private static void Publish(Action<AgentActivitySnapshot>? handlers, AgentActivitySnapshot? snapshot)
    {
        if (handlers is null || snapshot is null)
            return;

        foreach (Action<AgentActivitySnapshot> handler in handlers.GetInvocationList())
        {
            try { handler(snapshot); }
            catch (Exception ex) { Log.Warning(ex, "[activity] subscriber failed"); }
        }
    }

    private static string DisplayName(ProviderId provider) => provider switch
    {
        ProviderId.ClinePass => "Cline Pass",
        ProviderId.OpenCodeGo => "OpenCode Go",
        _ => provider.ToString(),
    };
}
