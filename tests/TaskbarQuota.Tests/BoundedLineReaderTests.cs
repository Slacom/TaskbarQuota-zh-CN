using System.IO;
using System.Linq;
using System.Text;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

public class BoundedLineReaderTests
{
    private static string[] Read(string text, int maxLineBytes)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return UsageHistoryService.ReadBoundedLines(stream, maxLineBytes).ToArray();
    }

    [Fact]
    public void Splits_lines_and_trims_carriage_returns()
        => Assert.Equal(new[] { "a", "bc", "d" }, Read("a\r\nbc\nd", 100));

    [Fact]
    public void Skips_lines_longer_than_the_limit()
        => Assert.Equal(new[] { "short", "after" }, Read("short\n" + new string('x', 50) + "\nafter\n", 10));

    [Fact]
    public void Skips_an_oversized_line_that_spans_read_buffers()
    {
        string huge = new string('x', 200_000);
        Assert.Equal(new[] { "before", "after" }, Read($"before\n{huge}\nafter", 1000));
    }

    [Fact]
    public void Keeps_a_line_split_across_read_buffers()
    {
        string line = new string('y', 100_000);
        Assert.Equal(new[] { line, "z" }, Read($"{line}\nz\n", 200_000));
    }

    [Fact]
    public void Decodes_multibyte_characters()
        => Assert.Equal(new[] { "héllo ✓" }, Read("héllo ✓\n", 100));
}
