using System;
using System.IO;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

public class HistoryFileVersionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("history-version-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Write_to_wal_companion_changes_version_while_main_file_is_unchanged()
    {
        string db = Path.Combine(_directory, "usage.db");
        File.WriteAllBytes(db, new byte[4096]);
        File.WriteAllBytes(db + "-wal", new byte[32]);
        var mainWriteTime = File.GetLastWriteTimeUtc(db);
        var before = UsageHistoryService.ReadFileVersion(db);

        File.AppendAllText(db + "-wal", "new rows");
        File.SetLastWriteTimeUtc(db, mainWriteTime);

        var after = UsageHistoryService.ReadFileVersion(db);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before.WriteTicks, after.WriteTicks);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void File_without_wal_companion_reports_zero_wal_state()
    {
        string log = Path.Combine(_directory, "session.jsonl");
        File.WriteAllText(log, "{}\n");

        var version = UsageHistoryService.ReadFileVersion(log);

        Assert.Equal(0, version.WalLength);
        Assert.Equal(0, version.WalWriteTicks);
    }
}
