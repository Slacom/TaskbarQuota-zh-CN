using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using TaskbarQuota.ActiveApp;
using TaskbarQuota.Diagnostics;

namespace TaskbarQuota.Services;

/// <summary>
/// Returns idle memory to the OS. A WinUI tray app touches most of its pages once at startup (XAML,
/// D3D driver, JIT, parse buffers) and then idles on a far smaller hot set; the rest stays resident
/// and counts against the working set. Compacting the GC heap (including the fragmented large-object
/// heap) and trimming the working set lets Windows reclaim those pages, which fault back in on demand.
/// Skipped while the user is interacting with the app's own windows.
/// </summary>
internal sealed class MemoryTrimmer : IDisposable
{
    private static readonly TimeSpan FirstTrimDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan TrimInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SkippedRetryDelay = TimeSpan.FromSeconds(30);

    private readonly Timer _timer;

    public MemoryTrimmer()
    {
        _timer = new Timer(_ =>
        {
            // One-shot re-armed each time so a skipped trim retries soon instead of waiting a full interval.
            var trimmed = Trim();
            try { _timer?.Change(trimmed ? TrimInterval : SkippedRetryDelay, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }, null, FirstTrimDelay, Timeout.InfiniteTimeSpan);
    }

    /// <returns>True when a trim ran (or failed); false when it was skipped to retry shortly.</returns>
    internal static bool Trim()
    {
        try
        {
            // Opening the flyout or dashboard right after a trim would pay the page-fault cost on the
            // click; the next interval trims instead.
            if (ActiveAppDetector.IsOwnProcessForeground())
            {
                Log.Debug("[memory] trim skipped: own window in foreground");
                return false;
            }

            var before = Environment.WorkingSet;

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            var ok = EmptyWorkingSet(GetCurrentProcess());
            Log.Debug($"[memory] trim: ok={ok} workingSet {before / 1048576} MB -> {Environment.WorkingSet / 1048576} MB");
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug($"[memory] trim failed: {ex.Message}");
            return true;
        }
    }

    public void Dispose() => _timer.Dispose();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
