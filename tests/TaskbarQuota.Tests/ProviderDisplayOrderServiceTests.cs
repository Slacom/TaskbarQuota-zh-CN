using System.Text.Json;
using TaskbarQuota.Usage;

namespace TaskbarQuota.Tests;

[Collection(WidgetRowSettingsCollection.Name)]
public class ProviderDisplayOrderServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("taskbarquota-order-").FullName;
    private readonly IDisposable _storage;
    private string SettingsPath => Path.Combine(_directory, "widget-provider-order.json");

    public ProviderDisplayOrderServiceTests()
    {
        _storage = AppStorage.OverrideAppDataDirectoryForTesting(_directory);
        ProviderDisplayOrderService.ResetForTesting();
    }

    public void Dispose()
    {
        ProviderDisplayOrderService.ResetForTesting();
        _storage.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_settings_use_enum_order_without_creating_a_file()
    {
        Assert.Equal(Enum.GetValues<ProviderId>(), ProviderDisplayOrderService.GetOrder());
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Order_survives_a_restart_and_is_stored_as_names()
    {
        var desired = Enum.GetValues<ProviderId>().Reverse().ToArray();

        ProviderDisplayOrderService.SetOrder(desired);
        ProviderDisplayOrderService.ResetForTesting();

        Assert.Equal(desired.Select(provider => provider.ToString()),
            JsonSerializer.Deserialize<string[]>(File.ReadAllText(SettingsPath))!);
        Assert.Equal(desired, ProviderDisplayOrderService.GetOrder());
    }

    [Fact]
    public void Loading_ignores_unknown_numeric_and_duplicate_ids_and_appends_missing_providers()
    {
        File.WriteAllText(SettingsPath, """
            ["Codex", "FutureProvider", "2", "codex", 3, null, "Claude"]
            """);

        var expected = new[] { ProviderId.Codex, ProviderId.Claude }
            .Concat(Enum.GetValues<ProviderId>().Except([ProviderId.Codex, ProviderId.Claude]));

        Assert.Equal(expected, ProviderDisplayOrderService.GetOrder());
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("{\"Codex\": 1}")]
    [InlineData("null")]
    public void Invalid_settings_fall_back_to_enum_order(string json)
    {
        File.WriteAllText(SettingsPath, json);

        Assert.Equal(Enum.GetValues<ProviderId>(), ProviderDisplayOrderService.GetOrder());
    }

    [Fact]
    public void Reordering_visible_subset_preserves_hidden_provider_slots()
    {
        ProviderDisplayOrderService.SetOrder([ProviderId.Codex, ProviderId.Cursor, ProviderId.Claude]);

        // Cursor is now hidden: dragging Claude before Codex must retain Cursor's slot.
        ProviderDisplayOrderService.SetOrder([ProviderId.Claude, ProviderId.Codex]);

        Assert.Equal(new[] { ProviderId.Claude, ProviderId.Cursor, ProviderId.Codex },
            ProviderDisplayOrderService.GetOrder().Take(3));
        Assert.Equal(new[] { ProviderId.Claude, ProviderId.Codex },
            ProviderDisplayOrderService.Sort([ProviderId.Codex, ProviderId.Claude]));
        Assert.Equal(new[] { ProviderId.Claude, ProviderId.Cursor, ProviderId.Codex },
            ProviderDisplayOrderService.Sort([ProviderId.Codex, ProviderId.Claude, ProviderId.Cursor]));
    }

    [Fact]
    public void Sort_is_independent_of_focus_order_and_does_not_add_absent_providers()
    {
        ProviderDisplayOrderService.SetOrder([ProviderId.Codex, ProviderId.Claude]);
        var expected = new[] { ProviderId.Codex, ProviderId.Claude };

        Assert.Equal(expected, ProviderDisplayOrderService.Sort([ProviderId.Claude, ProviderId.Codex]));
        Assert.Equal(expected, ProviderDisplayOrderService.Sort([ProviderId.Codex, ProviderId.Claude]));
        Assert.Equal(new[] { ProviderId.Claude }, ProviderDisplayOrderService.Sort([ProviderId.Claude]));
        Assert.Empty(ProviderDisplayOrderService.Sort([]));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Saved_order_applies_after_capacity_keeps_pins_selected(int capacity)
    {
        ProviderDisplayOrderService.SetOrder([ProviderId.Codex, ProviderId.Claude, ProviderId.Zai]);
        var candidates = UsageCoordinator.ComputeWidgetDisplayProviders(
            ProviderId.Codex,
            true,
            Array.Empty<ProviderId>(),
            ProviderDisplayOrderService.GetOrder(),
            provider => provider is ProviderId.Claude or ProviderId.Zai,
            _ => true,
            _ => true);

        var displayed = ProviderDisplayOrderService.Sort(candidates.Take(capacity));

        // The active provider sorts first when it fits, but cannot displace a pin to get a slot.
        Assert.Equal(capacity == 2
            ? new[] { ProviderId.Claude, ProviderId.Zai }
            : new[] { ProviderId.Codex, ProviderId.Claude, ProviderId.Zai }, displayed);
    }

    [Fact]
    public void SetOrder_ignores_invalid_ids_and_duplicates()
    {
        ProviderDisplayOrderService.SetOrder([(ProviderId)(-1), ProviderId.Codex, ProviderId.Codex, ProviderId.Claude, (ProviderId)999]);

        Assert.Equal(new[] { ProviderId.Codex, ProviderId.Claude }
            .Concat(Enum.GetValues<ProviderId>().Except([ProviderId.Codex, ProviderId.Claude])),
            ProviderDisplayOrderService.GetOrder());
    }

    [Fact]
    public void Changed_fires_only_when_the_effective_order_changes()
    {
        int notifications = 0;
        EventHandler handler = (_, _) => notifications++;
        ProviderDisplayOrderService.Changed += handler;
        try
        {
            ProviderDisplayOrderService.SetOrder(Enum.GetValues<ProviderId>());
            ProviderDisplayOrderService.SetOrder([]);
            ProviderDisplayOrderService.SetOrder([ProviderId.Codex]);
            Assert.Equal(0, notifications);

            ProviderDisplayOrderService.SetOrder([ProviderId.Codex, ProviderId.Claude]);
            Assert.Equal(1, notifications);

            ProviderDisplayOrderService.SetOrder([ProviderId.Codex, ProviderId.Codex, ProviderId.Claude, (ProviderId)999]);
            ProviderDisplayOrderService.GetOrder();
            ProviderDisplayOrderService.Sort([ProviderId.Claude, ProviderId.Codex]);
            Assert.Equal(1, notifications);
        }
        finally
        {
            ProviderDisplayOrderService.Changed -= handler;
        }
    }

    [Fact]
    public void Changing_storage_directories_reloads_the_matching_order()
    {
        ProviderDisplayOrderService.SetOrder([ProviderId.Codex, ProviderId.Claude]);
        string otherDirectory = Path.Combine(_directory, "other-profile");
        using (AppStorage.OverrideAppDataDirectoryForTesting(otherDirectory))
        {
            Assert.Equal(Enum.GetValues<ProviderId>(), ProviderDisplayOrderService.GetOrder());
            ProviderDisplayOrderService.SetOrder([ProviderId.Cursor, ProviderId.Codex, ProviderId.Claude]);
            Assert.Equal(ProviderId.Cursor, ProviderDisplayOrderService.GetOrder()[0]);
        }

        Assert.Equal(ProviderId.Codex, ProviderDisplayOrderService.GetOrder()[0]);
    }

    [Fact]
    public void Failed_save_keeps_live_order_and_can_retry_without_duplicate_notification()
    {
        // A directory in place of the settings file makes replacement fail deterministically.
        Directory.CreateDirectory(SettingsPath);
        int notifications = 0;
        EventHandler handler = (_, _) => notifications++;
        ProviderDisplayOrderService.Changed += handler;
        try
        {
            ProviderDisplayOrderService.SetOrder([ProviderId.Codex, ProviderId.Claude]);
            Assert.Equal(ProviderId.Codex, ProviderDisplayOrderService.GetOrder()[0]);
            Assert.Equal(1, notifications);

            Directory.Delete(SettingsPath);
            ProviderDisplayOrderService.SetOrder([ProviderId.Codex, ProviderId.Claude]);
            ProviderDisplayOrderService.ResetForTesting();

            Assert.Equal(ProviderId.Codex, ProviderDisplayOrderService.GetOrder()[0]);
            Assert.Equal(1, notifications);
            Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        }
        finally
        {
            ProviderDisplayOrderService.Changed -= handler;
        }
    }
}
