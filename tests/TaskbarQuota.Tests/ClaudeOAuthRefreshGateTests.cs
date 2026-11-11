using System;
using TaskbarQuota.Services;

namespace TaskbarQuota.Tests;

[Collection(WidgetRowSettingsCollection.Name)]
public class ClaudeOAuthRefreshGateTests : IDisposable
{
    public ClaudeOAuthRefreshGateTests() => ClaudeOAuth.ResetRefreshGateForTesting();

    public void Dispose() => ClaudeOAuth.ResetRefreshGateForTesting();

    [Fact]
    public void Refresh_scope_omits_the_scope_the_token_endpoint_rejects()
    {
        Assert.DoesNotContain("org:create_api_key", ClaudeOAuth.RefreshScope);
        Assert.Contains("user:profile", ClaudeOAuth.RefreshScope);
    }

    [Fact]
    public void Rejected_refresh_token_is_not_retried()
    {
        var now = DateTimeOffset.UtcNow;
        ClaudeOAuth.RecordRefreshFailure("refresh-a",
            "Claude token endpoint HTTP 400: {\"error\": \"invalid_scope\"}", now);

        Assert.False(ClaudeOAuth.CanAttemptRefresh("refresh-a", now.AddDays(1)));
        Assert.True(ClaudeOAuth.CanAttemptRefresh("refresh-b", now));
    }

    [Fact]
    public void Transient_refresh_failures_back_off()
    {
        var now = DateTimeOffset.UtcNow;
        ClaudeOAuth.RecordRefreshFailure("refresh-a", "Claude token endpoint HTTP 500: oops", now);

        Assert.False(ClaudeOAuth.CanAttemptRefresh("refresh-a", now.AddMinutes(4)));
        Assert.True(ClaudeOAuth.CanAttemptRefresh("refresh-a", now.AddMinutes(5)));
        Assert.Equal(TimeSpan.FromMinutes(10), ClaudeOAuth.RefreshBackoff(2));
        Assert.Equal(TimeSpan.FromHours(6), ClaudeOAuth.RefreshBackoff(20));
    }
}
