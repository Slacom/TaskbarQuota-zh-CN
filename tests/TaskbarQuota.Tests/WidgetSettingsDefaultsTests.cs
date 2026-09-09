using TaskbarQuota.Services;

namespace TaskbarQuota.Tests;

public sealed class WidgetSettingsDefaultsTests
{
    [Fact]
    public void MissingAutoHideSetting_DefaultsOffForNewUsers()
    {
        var path = Path.Combine(Path.GetTempPath(), "taskbarquota-auto-hide-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            Assert.False(WidgetSettingsService.LoadAutoHideUnavailableForTesting(path));

            File.WriteAllText(path, "1");
            Assert.True(WidgetSettingsService.LoadAutoHideUnavailableForTesting(path));

            File.WriteAllText(path, "0");
            Assert.False(WidgetSettingsService.LoadAutoHideUnavailableForTesting(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
