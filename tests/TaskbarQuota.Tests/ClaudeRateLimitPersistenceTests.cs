using System;
using System.IO;
using TaskbarQuota.Usage;
using TaskbarQuota.Usage.Providers;

namespace TaskbarQuota.Tests;

[Collection(WidgetRowSettingsCollection.Name)]
public class ClaudeRateLimitPersistenceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("claude-rate-limit-").FullName;
    private readonly IDisposable _storage;

    public ClaudeRateLimitPersistenceTests()
    {
        _storage = AppStorage.OverrideAppDataDirectoryForTesting(_directory);
        ClaudeProvider.ForgetInMemoryRateLimitForTesting();
    }

    public void Dispose()
    {
        ClaudeProvider.ForgetInMemoryRateLimitForTesting();
        _storage.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Rate_limit_survives_a_restart()
    {
        ClaudeProvider.RecordOAuthRateLimitUntil(DateTimeOffset.Now.AddHours(1));

        ClaudeProvider.ForgetInMemoryRateLimitForTesting();

        Assert.True(ClaudeProvider.IsOAuthRateLimited("token-a"));
    }

    [Fact]
    public void Expired_rate_limit_is_cleared_after_a_restart()
    {
        ClaudeProvider.RecordOAuthRateLimitUntil(DateTimeOffset.Now.AddSeconds(-1));

        ClaudeProvider.ForgetInMemoryRateLimitForTesting();

        Assert.False(ClaudeProvider.IsOAuthRateLimited("token-a"));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void No_saved_rate_limit_means_not_limited()
        => Assert.False(ClaudeProvider.IsOAuthRateLimited("token-a"));

    [Fact]
    public void Recent_result_is_reused_only_for_the_same_token()
    {
        var now = DateTimeOffset.Now;
        var result = new ProviderFetchResult(new UsageSnapshot(new RateWindow(40)), "oauth");
        ClaudeProvider.RememberOAuthResult("token-a", result, now);

        Assert.Same(result, ClaudeProvider.RecentOAuthResult("token-a", now.AddSeconds(10)));
        Assert.Null(ClaudeProvider.RecentOAuthResult("token-b", now.AddSeconds(10)));
        Assert.Same(result, ClaudeProvider.RecentOAuthResult("token-a", now.AddSeconds(110)));
        Assert.Null(ClaudeProvider.RecentOAuthResult("token-a", now.Add(ClaudeProvider.OAuthResultReuseWindow)));
    }

    [Fact]
    public void Legacy_timestamp_only_file_still_limits_every_token()
    {
        File.WriteAllText(Path.Combine(_directory, "claude-oauth-rate-limit.txt"),
            DateTimeOffset.Now.AddMinutes(30).ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(ClaudeProvider.IsOAuthRateLimited("token-a"));
        Assert.True(ClaudeProvider.IsOAuthRateLimited("token-b"));
    }

    [Fact]
    public void Rate_limit_belongs_to_the_token_that_hit_it()
    {
        var now = DateTimeOffset.Now;
        ClaudeProvider.RecordOAuthRateLimit("token-a", retryAfter: null, now);

        ClaudeProvider.ForgetInMemoryRateLimitForTesting();

        Assert.True(ClaudeProvider.IsOAuthRateLimited("token-a"));
        Assert.False(ClaudeProvider.IsOAuthRateLimited("token-b"));
    }

    [Fact]
    public void Retry_after_is_honoured_and_never_shortened()
    {
        var now = DateTimeOffset.Now;
        var hour = now.AddHours(1);

        Assert.Equal(hour, ClaudeProvider.RecordOAuthRateLimit("token-a", hour, now));
        Assert.Equal(hour, ClaudeProvider.RecordOAuthRateLimit("token-a", now.AddMinutes(1), now));
    }

    [Fact]
    public void Consecutive_rate_limits_without_retry_after_back_off()
    {
        var now = DateTimeOffset.Now;

        Assert.Equal(now.AddMinutes(5), ClaudeProvider.RecordOAuthRateLimit("token-a", null, now));
        Assert.Equal(now.AddMinutes(10), ClaudeProvider.RecordOAuthRateLimit("token-a", null, now));
        Assert.Equal(now.AddMinutes(20), ClaudeProvider.RecordOAuthRateLimit("token-a", null, now));
        Assert.Equal(now.AddMinutes(30), ClaudeProvider.RecordOAuthRateLimit("token-a", null, now));
        Assert.Equal(now.AddMinutes(30), ClaudeProvider.RecordOAuthRateLimit("token-a", null, now));
    }

    [Fact]
    public void Cli_refresh_waits_for_running_claude_code_then_takes_over()
    {
        var now = DateTimeOffset.Now;

        Assert.False(ClaudeProvider.ShouldDeferCliRefresh("token-a", claudeCodeRunning: false, now));
        Assert.True(ClaudeProvider.ShouldDeferCliRefresh("token-a", claudeCodeRunning: true, now));
        Assert.True(ClaudeProvider.ShouldDeferCliRefresh("token-a", claudeCodeRunning: true, now.AddMinutes(29)));
        Assert.False(ClaudeProvider.ShouldDeferCliRefresh("token-a", claudeCodeRunning: true, now.AddMinutes(31)));
        Assert.True(ClaudeProvider.ShouldDeferCliRefresh("token-b", claudeCodeRunning: true, now.AddMinutes(31)));
    }
}
