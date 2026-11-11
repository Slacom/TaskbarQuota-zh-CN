using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaskbarQuota.Usage;

namespace TaskbarQuota;

/// <summary>Persists a stable widget order independently of provider visibility and focus.</summary>
public static class ProviderDisplayOrderService
{
    private static readonly object Gate = new();
    private static readonly ProviderId[] DefaultOrder = Enum.GetValues<ProviderId>();
    private static readonly HashSet<ProviderId> KnownProviders = new(DefaultOrder);
    private static readonly Dictionary<string, ProviderId> ProvidersByName =
        DefaultOrder.ToDictionary(provider => provider.ToString(), StringComparer.OrdinalIgnoreCase);
    private static string? _loadedPath;
    private static ProviderId[] _order = DefaultOrder.ToArray();
    private static bool _needsSave;

    public static event EventHandler? Changed;

    /// <summary>Returns every known provider in its remembered order, including hidden providers.</summary>
    public static IReadOnlyList<ProviderId> GetOrder()
    {
        lock (Gate)
        {
            EnsureLoaded();
            return _order.ToArray();
        }
    }

    /// <summary>
    /// Reorders the supplied providers within their existing slots. Omitted providers keep their
    /// positions so temporarily hiding a provider does not discard its preferred placement.
    /// </summary>
    public static void SetOrder(IEnumerable<ProviderId> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var requested = providers.Where(KnownProviders.Contains).Distinct().ToArray();
        var included = requested.ToHashSet();
        bool changed;
        lock (Gate)
        {
            EnsureLoaded();
            int index = 0;
            var reordered = _order.Select(provider => included.Contains(provider) ? requested[index++] : provider).ToArray();
            changed = !_order.SequenceEqual(reordered);
            if (changed)
            {
                _order = reordered;
                _needsSave = true;
            }

            if (_needsSave)
                Save();
        }

        if (changed)
            Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Orders a provider subset without introducing providers absent from that subset.</summary>
    public static IReadOnlyList<ProviderId> Sort(IEnumerable<ProviderId> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var included = providers.Where(KnownProviders.Contains).ToHashSet();
        lock (Gate)
        {
            EnsureLoaded();
            return _order.Where(included.Contains).ToArray();
        }
    }

    private static void EnsureLoaded()
    {
        string path = Path.Combine(AppStorage.AppDataDirectory, "widget-provider-order.json");
        if (string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            return;

        _loadedPath = path;
        _order = Load(path);
        _needsSave = false;
    }

    private static ProviderId[] Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var loaded = new List<ProviderId>();
                    var seen = new HashSet<ProviderId>();
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        // Match enum names only: Enum.TryParse also accepts numeric strings.
                        if (item.ValueKind == JsonValueKind.String
                            && ProvidersByName.TryGetValue(item.GetString()!, out var provider)
                            && seen.Add(provider))
                        {
                            loaded.Add(provider);
                        }
                    }

                    loaded.AddRange(DefaultOrder.Where(provider => !seen.Contains(provider)));
                    return loaded.ToArray();
                }
            }
        }
        catch
        {
            // Missing, unreadable, or malformed settings use the deterministic default order.
        }

        return DefaultOrder.ToArray();
    }

    // Called under Gate so writes and the in-memory order cannot overtake each other.
    private static void Save()
    {
        string path = _loadedPath!;
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(
                _order.Select(provider => provider.ToString()),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
            _needsSave = false;
        }
        catch
        {
            // Keep the in-memory preference; a later SetOrder call can retry the write.
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch { /* Best-effort cleanup after a failed write. */ }
        }
    }

    internal static void ResetForTesting()
    {
        lock (Gate)
        {
            _loadedPath = null;
            _order = DefaultOrder.ToArray();
            _needsSave = false;
        }
    }
}
