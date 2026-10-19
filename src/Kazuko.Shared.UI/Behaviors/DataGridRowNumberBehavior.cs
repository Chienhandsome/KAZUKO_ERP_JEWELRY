using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Kazuko.Shared.UI.Behaviors;

/// <summary>1-based display indexes, independent of virtualization's AlternationIndex.</summary>
public static class DataGridRowNumberBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DataGridRowNumberBehavior), new PropertyMetadata(false, OnChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(DataGridRowNumberBehavior));
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not DataGrid grid) return;
        (grid.GetValue(StateProperty) as State)?.Dispose();
        grid.SetValue(StateProperty, (bool)e.NewValue ? new State(grid) : null);
    }

    private sealed class State : IDisposable
    {
        private readonly DataGrid _grid;
        private DispatcherOperation? _refresh;
        private bool _attached;
        public State(DataGrid grid)
        {
            _grid = grid;
            grid.Loaded += OnLoaded;
            grid.Unloaded += OnUnloaded;
            Attach();
        }
        private void OnLoaded(object sender, RoutedEventArgs e) => Attach();
        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();
        private void Attach()
        {
            if (_attached) return;
            _attached = true;
            _grid.LoadingRow += OnLoadingRow;
            ((INotifyCollectionChanged)_grid.Items).CollectionChanged += OnItemsChanged;
            QueueRefresh();
        }
        private void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _grid.LoadingRow -= OnLoadingRow;
            ((INotifyCollectionChanged)_grid.Items).CollectionChanged -= OnItemsChanged;
            _refresh?.Abort();
            _refresh = null;
        }
        private static void OnLoadingRow(object? sender, DataGridRowEventArgs e) => e.Row.Header = e.Row.GetIndex() + 1;
        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();
        private void QueueRefresh()
        {
            if (_refresh?.Status == DispatcherOperationStatus.Pending) return;
            _refresh = _grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _refresh = null;
                for (var index = 0; index < _grid.Items.Count; index++)
                    if (_grid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row)
                        row.Header = index + 1;
            }));
        }
        public void Dispose()
        {
            Detach();
            _grid.Loaded -= OnLoaded;
            _grid.Unloaded -= OnUnloaded;
        }
    }
}
