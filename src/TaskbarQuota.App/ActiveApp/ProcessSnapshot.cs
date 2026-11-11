using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace TaskbarQuota.ActiveApp
{
    internal readonly record struct ProcessEntry(int Pid, int ParentPid, string ExeName, long CreateFileTime = 0);

    /// <summary>
    /// Cheap process enumeration shared by the foreground detector and the activity scanner.
    /// One NtQuerySystemInformation call replaces <c>Process.GetProcesses()</c> (one managed Process object per running
    /// process) and the unfiltered-by-cost WMI <c>Win32_Process</c> queries. Start times and command
    /// lines of a live process never change, so they are cached per PID and only fetched once.
    /// </summary>
    internal static class ProcessSnapshot
    {
        // A snapshot is ~1 ms; this TTL only lets the detector and scanner share one within a tick.
        private static readonly TimeSpan SnapshotTtl = TimeSpan.FromMilliseconds(200);
        private static readonly object Gate = new();

        private static IReadOnlyList<ProcessEntry> _entries = Array.Empty<ProcessEntry>();
        private static long _capturedAtTicks;

        // Keyed by pid and validated against the parent pid so a recycled pid is re-read.
        private static readonly Dictionary<int, (int ParentPid, DateTime Started)> StartCache = new();
        private static readonly Dictionary<int, (int ParentPid, string Name, string CommandLine)> CommandLineCache = new();

        private const long NameCacheTtlMs = 5000;
        private static readonly object NameGate = new();
        private static readonly Dictionary<int, (string? Name, long AtTicks)> NameCache = new();

        public static IReadOnlyList<ProcessEntry> Capture()
        {
            lock (Gate)
            {
                var now = Environment.TickCount64;
                if (_capturedAtTicks != 0 && now - _capturedAtTicks < SnapshotTtl.TotalMilliseconds)
                    return _entries;

                var list = new List<ProcessEntry>(512);
                if (!TryEnumerate(list))
                    return _entries;

                _entries = list;
                _capturedAtTicks = now;
                Prune(list);
                return _entries;
            }
        }

        // SYSTEM_PROCESS_INFORMATION field offsets (identical on x64 and ARM64).
        private const int OffsetNextEntry = 0x00;
        private const int OffsetCreateTime = 0x20;
        private const int OffsetImageNameLength = 0x38;
        private const int OffsetImageNameBuffer = 0x40;
        private const int OffsetProcessId = 0x50;
        private const int OffsetParentProcessId = 0x58;
        private const int SystemProcessInformationClass = 5;
        private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

        // One NtQuerySystemInformation call is about half the cost of walking a ToolHelp snapshot,
        // which makes a separate kernel call per process.
        // One buffer reused for every snapshot (callers hold Gate). Renting from ArrayPool instead leaves a
        // 1 MB large-object-heap array behind per thread that ever captured.
        private static byte[] _enumerateBuffer = new byte[1 << 20];

        private static unsafe bool TryEnumerate(List<ProcessEntry> list)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var buffer = _enumerateBuffer;
                fixed (byte* start = buffer)
                {
                    var status = NtQuerySystemInformation(SystemProcessInformationClass, start, buffer.Length, out var needed);
                    if (status == StatusInfoLengthMismatch)
                    {
                        _enumerateBuffer = new byte[Math.Max(needed + 65536, buffer.Length * 2)];
                        continue;
                    }
                    if (status < 0)
                        return false;

                    var current = start;
                    while (true)
                    {
                        var pid = (int)*(nint*)(current + OffsetProcessId);
                        var parent = (int)*(nint*)(current + OffsetParentProcessId);
                        var length = *(ushort*)(current + OffsetImageNameLength);
                        var name = *(char**)(current + OffsetImageNameBuffer);
                        var created = *(long*)(current + OffsetCreateTime);
                        list.Add(new ProcessEntry(pid, parent, name == null ? "" : new string(name, 0, length / 2), created));

                        var next = *(uint*)(current + OffsetNextEntry);
                        if (next == 0) break;
                        current += next;
                    }
                    return true;
                }
            }

            return false;
        }

        /// <summary>Process creation time (local), or <see cref="DateTime.MinValue"/> when unreadable.</summary>
        public static DateTime GetStartTime(int pid, int parentPid)
        {
            lock (Gate)
            {
                if (StartCache.TryGetValue(pid, out var cached) && cached.ParentPid == parentPid)
                    return cached.Started;

                // The process list already carries every process's creation time (no handle needed, so
                // protected processes that OpenProcess refuses still resolve).
                DateTime started = DateTime.MinValue;
                foreach (var entry in _entries)
                {
                    if (entry.Pid != pid) continue;
                    started = FromFileTime(entry.CreateFileTime);
                    break;
                }
                if (started == DateTime.MinValue)
                    started = ReadStartTime(pid);

                StartCache[pid] = (parentPid, started);
                return started;
            }
        }

        private static DateTime FromFileTime(long fileTime)
        {
            try { return fileTime > 0 ? DateTime.FromFileTime(fileTime) : DateTime.MinValue; }
            catch (ArgumentOutOfRangeException) { return DateTime.MinValue; }
        }

        /// <summary>
        /// Name + command line for each pid, reading WMI only for pids not seen before.
        /// <paramref name="pids"/> pairs a pid with its current parent pid for cache validation.
        /// </summary>
        public static Dictionary<int, (string Name, string CommandLine)> GetCommandLines(
            IEnumerable<(int Pid, int ParentPid)> pids)
        {
            var result = new Dictionary<int, (string Name, string CommandLine)>();
            var missing = new List<(int Pid, int ParentPid)>();
            lock (Gate)
            {
                foreach (var (pid, parentPid) in pids)
                {
                    if (result.ContainsKey(pid)) continue;
                    if (CommandLineCache.TryGetValue(pid, out var cached) && cached.ParentPid == parentPid)
                        result[pid] = (cached.Name, cached.CommandLine);
                    else
                        missing.Add((pid, parentPid));
                }
            }

            const int chunkSize = 60;
            for (int i = 0; i < missing.Count; i += chunkSize)
            {
                var chunk = missing.GetRange(i, Math.Min(chunkSize, missing.Count - i));
                var where = string.Join(" OR ", chunk.ConvertAll(p => "ProcessId = " + p.Pid));
                var parents = new Dictionary<int, int>();
                foreach (var (pid, parentPid) in chunk) parents[pid] = parentPid;
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE " + where);
                    using var processes = searcher.Get();
                    foreach (ManagementObject mo in processes)
                    {
                        using (mo)
                        {
                            if (!int.TryParse(mo["ProcessId"]?.ToString(), out var pid)) continue;
                            var name = mo["Name"] as string ?? "";
                            var commandLine = mo["CommandLine"] as string ?? "";
                            result[pid] = (name, commandLine);
                            if (parents.TryGetValue(pid, out var parentPid))
                            {
                                lock (Gate)
                                    CommandLineCache[pid] = (parentPid, name, commandLine);
                            }
                        }
                    }
                }
                catch (ManagementException) { }
                catch (UnauthorizedAccessException) { }
            }

            return result;
        }

        /// <summary>
        /// Base name (no extension) of a live process, equal to <c>Process.ProcessName</c>. Reads the
        /// image path through a limited handle (no managed Process object); falls back to the managed
        /// lookup for processes whose path cannot be queried (protected / access denied).
        /// </summary>
        public static string? TryGetProcessName(int pid)
        {
            if (pid <= 0) return null;

            // The foreground/focus walk asks for the same few pids every tick, and opening a process
            // handle is the expensive part. A short TTL keeps pid reuse from being misattributed.
            var now = Environment.TickCount64;
            lock (NameGate)
            {
                if (NameCache.TryGetValue(pid, out var hit) && now - hit.AtTicks < NameCacheTtlMs)
                    return hit.Name;
            }

            var resolved = ResolveProcessName(pid);
            lock (NameGate)
            {
                if (NameCache.Count > 512) NameCache.Clear();
                NameCache[pid] = (resolved, now);
            }
            return resolved;
        }

        private static string? ResolveProcessName(int pid)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var buffer = new char[1024];
                    uint size = (uint)buffer.Length;
                    if (QueryFullProcessImageNameW(handle, 0, buffer, ref size) && size > 0)
                    {
                        var path = new string(buffer, 0, (int)size);
                        var name = System.IO.Path.GetFileNameWithoutExtension(path);
                        if (!string.IsNullOrEmpty(name)) return name;
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                return process.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        private static void Prune(List<ProcessEntry> live)
        {
            if (StartCache.Count == 0 && CommandLineCache.Count == 0) return;
            var alive = new HashSet<int>(live.Count);
            foreach (var e in live) alive.Add(e.Pid);

            List<int>? stale = null;
            foreach (var pid in StartCache.Keys)
                if (!alive.Contains(pid)) (stale ??= new()).Add(pid);
            if (stale != null) foreach (var pid in stale) StartCache.Remove(pid);

            stale = null;
            foreach (var pid in CommandLineCache.Keys)
                if (!alive.Contains(pid)) (stale ??= new()).Add(pid);
            if (stale != null) foreach (var pid in stale) CommandLineCache.Remove(pid);
        }

        private static DateTime ReadStartTime(int pid)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
            if (handle == IntPtr.Zero) return DateTime.MinValue;
            try
            {
                if (!GetProcessTimes(handle, out var creation, out _, out _, out _)) return DateTime.MinValue;
                var fileTime = ((long)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
                return fileTime > 0 ? DateTime.FromFileTime(fileTime) : DateTime.MinValue;
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MinValue;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("ntdll.dll")]
        private static extern unsafe int NtQuerySystemInformation(int informationClass, byte* buffer, int length, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(
            IntPtr process,
            out System.Runtime.InteropServices.ComTypes.FILETIME creation,
            out System.Runtime.InteropServices.ComTypes.FILETIME exit,
            out System.Runtime.InteropServices.ComTypes.FILETIME kernel,
            out System.Runtime.InteropServices.ComTypes.FILETIME user);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] exeName, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
