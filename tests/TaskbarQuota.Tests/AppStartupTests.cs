using TaskbarQuota;
using Microsoft.Windows.AppLifecycle;

namespace TaskbarQuota.Tests;

public class AppStartupTests
{
    [Fact]
    public void IsWidgetStartup_WhenActivationArgumentsContainStartupFlag_ReturnsTrue()
    {
        Assert.True(App.IsWidgetStartup("--startup-widget"));
    }

    [Fact]
    public void IsWidgetStartup_WhenCommandLineArgumentsContainStartupFlag_ReturnsTrue()
    {
        Assert.True(App.IsWidgetStartup(null, ["TaskbarQuota.exe", "--startup-widget"]));
    }

    [Fact]
    public void IsWidgetStartup_WhenNoStartupFlag_ReturnsFalse()
    {
        Assert.False(App.IsWidgetStartup(null, ["TaskbarQuota.exe"]));
    }

    [Fact]
    public void IsWidgetStartup_WhenActivatedByStartupTask_ReturnsTrue()
    {
        Assert.True(App.IsWidgetStartup(null, ["TaskbarQuota.exe"], ExtendedActivationKind.StartupTask));
        Assert.False(App.ShouldSurfaceWindowOnActivation(null, ExtendedActivationKind.StartupTask));
    }

    [Fact]
    public void ShouldSurfaceWindowOnActivation_ForStoreOpen_ReturnsTrue()
    {
        Assert.True(App.ShouldSurfaceWindowOnActivation(null));
        Assert.True(App.ShouldSurfaceWindowOnActivation(string.Empty));
    }

    [Fact]
    public void ShouldSurfaceWindowOnActivation_ForStartupWidgetLaunch_ReturnsFalse()
    {
        Assert.False(App.ShouldSurfaceWindowOnActivation("--startup-widget"));
    }

    [Fact]
    public void ShouldRetryTaskbarInitialization_AllowsStartupWidgetRetries()
    {
        Assert.True(App.ShouldRetryTaskbarInitialization(1));
        Assert.True(App.TaskbarInitializationMaxAttempts > 1);
        Assert.False(App.ShouldRetryTaskbarInitialization(App.TaskbarInitializationMaxAttempts));
    }

    [Fact]
    public void ShouldRetryTaskbarInitialization_BoundaryConditions()
    {
        Assert.True(App.ShouldRetryTaskbarInitialization(0));
        Assert.True(App.ShouldRetryTaskbarInitialization(App.TaskbarInitializationMaxAttempts - 1));
        Assert.False(App.ShouldRetryTaskbarInitialization(App.TaskbarInitializationMaxAttempts + 1));
    }
}
