using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskbarQuota.ViewModels;

namespace TaskbarQuota.Views
{
    public sealed partial class SettingsPage
    {
        private readonly ObservableCollection<ProviderSettingItemViewModel> _providerOrder = new();
        private bool _isDraggingProviderOrder;

        private void InitializeProviderOrder()
        {
            ProviderOrderList.ItemsSource = _providerOrder;
            Loaded += OnProviderOrderPageLoaded;
            Unloaded += OnProviderOrderPageUnloaded;
        }

        private void OnProviderOrderPageLoaded(object sender, RoutedEventArgs e)
        {
            WidgetSettingsService.Changed += OnProviderOrderSettingsChanged;
            ProviderDisplayOrderService.Changed += OnProviderOrderSettingsChanged;
            RefreshProviderOrder();
        }

        private void OnProviderOrderPageUnloaded(object sender, RoutedEventArgs e)
        {
            WidgetSettingsService.Changed -= OnProviderOrderSettingsChanged;
            ProviderDisplayOrderService.Changed -= OnProviderOrderSettingsChanged;
        }

        private void OnProviderOrderSettingsChanged(object? sender, System.EventArgs e)
            => DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded && !_isDraggingProviderOrder)
                    RefreshProviderOrder();
            });

        private void RefreshProviderOrder()
        {
            if (_isDraggingProviderOrder)
                return;

            var selected = (ProviderOrderList.SelectedItem as ProviderSettingItemViewModel)?.Id;
            var providers = ViewModel.Providers.ToDictionary(item => item.Id);
            var ordered = ProviderDisplayOrderService.GetOrder()
                .Where(id => providers.ContainsKey(id) && WidgetSettingsService.IsProviderVisible(id))
                .Select(id => providers[id])
                .ToArray();
            if (!_providerOrder.SequenceEqual(ordered))
            {
                _providerOrder.Clear();
                foreach (var item in ordered)
                    _providerOrder.Add(item);
                ProviderOrderList.SelectedItem = _providerOrder.FirstOrDefault(item => item.Id == selected);
            }
            ProviderOrderEmptyText.Visibility = _providerOrder.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateProviderOrderButtons();
        }

        private void OnProviderOrderDragStarting(object sender, DragItemsStartingEventArgs e)
            => _isDraggingProviderOrder = true;

        private void OnProviderOrderDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            _isDraggingProviderOrder = false;
            SaveProviderOrder();
        }

        private void SaveProviderOrder()
        {
            ProviderDisplayOrderService.SetOrder(_providerOrder.Select(item => item.Id));
            RefreshProviderOrder();
        }

        private void OnProviderOrderSelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateProviderOrderButtons();

        private void UpdateProviderOrderButtons()
        {
            // SelectionChanged can fire while InitializeComponent is still creating the buttons.
            if (ProviderOrderUpButton is null || ProviderOrderDownButton is null)
                return;
            int selected = ProviderOrderList.SelectedIndex;
            ProviderOrderUpButton.IsEnabled = selected > 0;
            ProviderOrderDownButton.IsEnabled = selected >= 0 && selected < _providerOrder.Count - 1;
        }

        private void OnProviderOrderMoveUp(object sender, RoutedEventArgs e) => MoveProviderOrder(-1);

        private void OnProviderOrderMoveDown(object sender, RoutedEventArgs e) => MoveProviderOrder(1);

        private void MoveProviderOrder(int delta)
        {
            int from = ProviderOrderList.SelectedIndex;
            int to = from + delta;
            if (from < 0 || to < 0 || to >= _providerOrder.Count)
                return;
            var item = _providerOrder[from];
            _providerOrder.Move(from, to);
            ProviderOrderList.SelectedItem = item;
            SaveProviderOrder();
        }
    }
}
