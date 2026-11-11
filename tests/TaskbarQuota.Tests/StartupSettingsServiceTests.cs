using TaskbarQuota;
using Windows.ApplicationModel;

namespace TaskbarQuota.Tests;

public class StartupSettingsServiceTests
{
    [Fact]
    public void RunValueMustLaunchTheCurrentExecutable()
    {
        Assert.True(StartupSettingsService.IsRunValueForExecutable(
            "\"C:\\Apps\\Current\\TaskbarQuota.exe\" --startup-widget",
            "C:\\Apps\\Current\\TaskbarQuota.exe"));
        Assert.False(StartupSettingsService.IsRunValueForExecutable(
            "\"C:\\Apps\\Old\\TaskbarQuota.exe\" --startup-widget",
            "C:\\Apps\\Current\\TaskbarQuota.exe"));
    }

    [Theory]
    [InlineData(StartupTaskState.Enabled, true)]
    [InlineData(StartupTaskState.EnabledByPolicy, true)]
    [InlineData(StartupTaskState.Disabled, false)]
    [InlineData(StartupTaskState.DisabledByUser, false)]
    [InlineData(StartupTaskState.DisabledByPolicy, false)]
    public void EnabledStateMatchesWindowsRegistration(StartupTaskState state, bool expected)
        => Assert.Equal(expected, StartupSettingsService.IsEnabledState(state));

    [Fact]
    public void OlderInstallerStartupIsRecognizedWithoutFlaggingTheCurrentExecutable()
    {
        const string current = @"C:\Apps\Store\TaskbarQuota.exe";
        Assert.True(StartupSettingsService.IsOlderInstallerRunValue(
            "\"C:\\Apps\\Old\\TaskbarQuota.exe\" --startup-widget", current));
        Assert.False(StartupSettingsService.IsOlderInstallerRunValue(
            "\"C:\\Apps\\Store\\TaskbarQuota.exe\" --startup-widget", current));
        Assert.False(StartupSettingsService.IsOlderInstallerRunValue(
            "\"C:\\Apps\\Old\\TaskbarQuota.exe\"", current));
    }
}
