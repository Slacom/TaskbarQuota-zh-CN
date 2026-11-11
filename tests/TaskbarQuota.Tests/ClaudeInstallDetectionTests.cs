using TaskbarQuota;

namespace TaskbarQuota.Tests;

public sealed class ClaudeInstallDetectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TaskbarQuota-Claude-install-" + Guid.NewGuid());
    private string Profile => Path.Combine(_root, "profile");
    private string Local => Path.Combine(_root, "local");
    private string Roaming => Path.Combine(_root, "roaming");

    [Fact]
    public void DesktopInstallationWithoutStandaloneCliIsDetected()
    {
        var executable = Path.Combine(Local, "AnthropicClaude", "app-2.0.0", "claude.exe");
        CreateFile(executable);

        Assert.False(File.Exists(Path.Combine(Profile, ".claude", ".credentials.json")));
        Assert.False(Directory.Exists(Path.Combine(Local, "Programs", "Claude")));
        Assert.True(ProviderInstallDetector.IsClaudeInstalledWithoutCli(Profile, Local, Roaming));
    }

    [Fact]
    public void DesktopConfigurationIsDetectedWithoutReadingCredentials()
    {
        CreateFile(Path.Combine(Roaming, "Claude", "config.json"));

        Assert.True(ProviderInstallDetector.IsClaudeInstalledWithoutCli(Profile, Local, Roaming));
    }

    [Fact]
    public void StandaloneCliCredentialsRemainSupported()
    {
        CreateFile(Path.Combine(Profile, ".claude", ".credentials.json"));

        Assert.True(ProviderInstallDetector.IsClaudeInstalledWithoutCli(Profile, Local, Roaming));
    }

    [Fact]
    public void LegacyDesktopInstallationRemainsSupported()
    {
        Directory.CreateDirectory(Path.Combine(Local, "Programs", "Claude"));

        Assert.True(ProviderInstallDetector.IsClaudeInstalledWithoutCli(Profile, Local, Roaming));
    }

    [Fact]
    public void UnrelatedFilesDoNotCountAsAnInstallation()
    {
        CreateFile(Path.Combine(Roaming, "OtherApp", "config.json"));
        Directory.CreateDirectory(Path.Combine(Profile, ".claude"));

        Assert.False(ProviderInstallDetector.IsClaudeInstalledWithoutCli(Profile, Local, Roaming));
    }

    private static void CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
