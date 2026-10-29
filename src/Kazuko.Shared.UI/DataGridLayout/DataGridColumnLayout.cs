using Kazuko.Core.Settings;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;

namespace Kazuko.Shared.UI.DataGridLayout;

/// <summary>
/// Reusable DataGrid column layout: attached Key, LocalAppData persistence,
/// header context menu (Cột hiển thị / Dãn cột), and optional auto-fit on first load.
/// </summary>
public static class DataGridColumnLayout
{
    private static readonly DependencyProperty StoreProperty = DependencyProperty.RegisterAttached(
        "Store", typeof(ITableColumnLayoutStore), typeof(DataGridColumnLayout));
    private static readonly DependencyProperty AutoSaveProperty = DependencyProperty.RegisterAttached(
        "AutoSave", typeof(AutoSaveSubscription), typeof(DataGridColumnLayout));

    public static void Persist(DataGrid grid)
    {
        var store = (ITableColumnLayoutStore?)grid.GetValue(StoreProperty) ?? DataGridColumnLayoutStore.Current;
        store?.Save(GetSettingsKey(grid)!, Capture(grid));
    }

    /// <summary>Flush debounce timers before a host awaits database synchronization.</summary>
    public static void FlushPendingLayouts(DependencyObject root)
    {
        if (root is DataGrid grid && grid.GetValue(AutoSaveProperty) is AutoSaveSubscription subscription)
            subscription.Save();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            FlushPendingLayouts(VisualTreeHelper.GetChild(root, index));
    }

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(DataGridColumnLayout),
        new PropertyMetadata(null));

    public static readonly DependencyProperty SettingsKeyProperty = DependencyProperty.RegisterAttached(
        "SettingsKey",
        typeof(string),
        typeof(DataGridColumnLayout),
        new PropertyMetadata(null, OnSettingsKeyChanged));

    public static readonly DependencyProperty EnableHeaderMenuProperty = DependencyProperty.RegisterAttached(
        "EnableHeaderMenu",
        typeof(bool),
        typeof(DataGridColumnLayout),
        new PropertyMetadata(false, OnEnableHeaderMenuChanged));

    public static readonly DependencyProperty AutoFitOnFirstLoadProperty = DependencyProperty.RegisterAttached(
        "AutoFitOnFirstLoad",
        typeof(bool),
        typeof(DataGridColumnLayout),
        new PropertyMetadata(false));

    private static readonly DependencyProperty IsInitializedProperty = DependencyProperty.RegisterAttached(
        "IsInitialized",
        typeof(bool),
        typeof(DataGridColumnLayout),
        new PropertyMetadata(false));

    private static readonly DependencyProperty HeaderMenuAttachedProperty = DependencyProperty.RegisterAttached(
        "HeaderMenuAttached",
        typeof(bool),
        typeof(DataGridColumnLayout),
        new PropertyMetadata(false));

    public static void SetKey(DependencyObject element, string? value) =>
        element.SetValue(KeyProperty, value);

    public static string? GetKey(DependencyObject element) =>
        (string?)element.GetValue(KeyProperty);

    public static void SetSettingsKey(DependencyObject element, string? value) =>
        element.SetValue(SettingsKeyProperty, value);

    public static string? GetSettingsKey(DependencyObject element) =>
        (string?)element.GetValue(SettingsKeyProperty);

    public static void SetEnableHeaderMenu(DependencyObject element, bool value) =>
        element.SetValue(EnableHeaderMenuProperty, value);

    public static bool GetEnableHeaderMenu(DependencyObject element) =>
        (bool)element.GetValue(EnableHeaderMenuProperty);

    public static void SetAutoFitOnFirstLoad(DependencyObject element, bool value) =>
        element.SetValue(AutoFitOnFirstLoadProperty, value);

    public static bool GetAutoFitOnFirstLoad(DependencyObject element) =>
        (bool)element.GetValue(AutoFitOnFirstLoadProperty);

    public static string GetColumnKey(DataGridColumn column)
    {
        var attached = GetKey(column);
        if (!string.IsNullOrWhiteSpace(attached))
            return attached.Trim();

        return ResolveHeader(column);
    }

    public static string ResolveHeader(DataGridColumn column) =>
        column.Header switch
        {
            string text => text,
            null => string.Empty,
            _ => column.Header.ToString() ?? string.Empty
        };

    public static void Apply(DataGrid dataGrid, DataGridColumnLayoutSettings? settings)
    {
        if (dataGrid is null || settings?.Columns is null || settings.Columns.Count == 0)
            return;

        var byKey = dataGrid.Columns
            .Select(column => (Column: column, Key: GetColumnKey(column)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Column, StringComparer.OrdinalIgnoreCase);

        var orderedKeys = settings.Columns
            .OrderBy(static c => c.DisplayIndex)
            .Select(static c => c.Key)
            .Where(byKey.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var column in dataGrid.Columns)
        {
            var key = GetColumnKey(column);
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!orderedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                orderedKeys.Add(key);
        }

        for (var index = 0; index < orderedKeys.Count; index++)
        {
            if (!byKey.TryGetValue(orderedKeys[index], out var column))
                continue;

            column.DisplayIndex = index;
        }

        foreach (var saved in settings.Columns)
        {
            if (!byKey.TryGetValue(saved.Key, out var column))
                continue;

            column.Visibility = saved.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            if (saved.Width is > 0 && double.IsFinite(saved.Width.Value))
                column.Width = new DataGridLength(saved.Width.Value);
        }
    }

    public static DataGridColumnLayoutSettings Capture(DataGrid dataGrid)
    {
        var columns = dataGrid.Columns
            .OrderBy(static c => c.DisplayIndex)
            .Select((column, index) =>
            {
                double? width = null;
                if (column.Width.UnitType == DataGridLengthUnitType.Pixel && column.Width.Value > 0)
                    width = column.Width.Value;
                else if (column.ActualWidth > 0 && column.Width.IsAbsolute)
                    width = column.ActualWidth;

                return new DataGridColumnSetting(
                    GetColumnKey(column),
                    column.Visibility == Visibility.Visible,
                    index,
                    width);
            })
            .Where(static c => !string.IsNullOrWhiteSpace(c.Key))
            .ToArray();

        return new DataGridColumnLayoutSettings(columns);
    }

    public static void FitColumns(DataGrid dataGrid)
    {
        if (dataGrid is null)
            return;

        dataGrid.UpdateLayout();

        foreach (var column in dataGrid.Columns)
        {
            if (column.Visibility != Visibility.Visible)
                continue;

            column.Width = new DataGridLength(1, DataGridLengthUnitType.SizeToHeader);
            dataGrid.UpdateLayout();
            var headerWidth = column.ActualWidth;

            column.Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells);
            dataGrid.UpdateLayout();
            var cellsWidth = column.ActualWidth;

            var fitted = Math.Max(headerWidth, cellsWidth) + 12;
            if (fitted < 48)
                fitted = 48;

            column.Width = new DataGridLength(fitted);
        }
    }

    public static void ShowColumnVisibilityDialog(DataGrid dataGrid)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);

        var settingsKey = GetSettingsKey(dataGrid);
        if (string.IsNullOrWhiteSpace(settingsKey))
            throw new InvalidOperationException("DataGridColumnLayout.SettingsKey is required.");

        var owner = Window.GetWindow(dataGrid);
        var dialog = new DataGridColumnVisibilityDialog(dataGrid, settingsKey)
        {
            Owner = owner
        };
        dialog.ShowDialog();
    }

    public static void FitColumnsAndPersist(DataGrid dataGrid)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);

        FitColumns(dataGrid);

        var settingsKey = GetSettingsKey(dataGrid);
        if (!string.IsNullOrWhiteSpace(settingsKey))
            Persist(dataGrid);
    }

    private static void OnSettingsKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        ScheduleInitialize(grid);
    }

    private static void OnEnableHeaderMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        if (GetEnableHeaderMenu(grid) && (bool)grid.GetValue(IsInitializedProperty))
            AttachHeaderMenu(grid);
        else
            ScheduleInitialize(grid);
    }

    private static void ScheduleInitialize(DataGrid grid)
    {
        if (string.IsNullOrWhiteSpace(GetSettingsKey(grid)))
            return;

        if (grid.IsLoaded)
            Initialize(grid);
        else
        {
            void OnLoaded(object sender, RoutedEventArgs args)
            {
                grid.Loaded -= OnLoaded;
                Initialize(grid);
            }

            grid.Loaded += OnLoaded;
        }
    }

    private static void Initialize(DataGrid grid)
    {
        if ((bool)grid.GetValue(IsInitializedProperty))
        {
            if (GetEnableHeaderMenu(grid))
                AttachHeaderMenu(grid);
            return;
        }

        grid.SetValue(IsInitializedProperty, true);

        var settingsKey = GetSettingsKey(grid);
        if (string.IsNullOrWhiteSpace(settingsKey))
            return;

        var store = DataGridColumnLayoutStore.Current;
        grid.SetValue(StoreProperty, store);
        var settings = store?.Load(settingsKey);
        if (settings is not null)
            Apply(grid, settings);
        else if (GetAutoFitOnFirstLoad(grid))
            ScheduleAutoFit(grid);

        if (GetEnableHeaderMenu(grid))
            AttachHeaderMenu(grid);

        AttachAutoSave(grid);
    }

    private static void AttachAutoSave(DataGrid grid) => grid.SetValue(AutoSaveProperty, new AutoSaveSubscription(grid));

    private sealed class AutoSaveSubscription
    {
        private readonly DataGrid _grid;
        private readonly DispatcherTimer _timer;
        private readonly HashSet<DataGridColumn> _observed = [];
        private readonly DependencyPropertyDescriptor[] _descriptors =
        [
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))!,
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.VisibilityProperty, typeof(DataGridColumn))!
        ];
        private bool _dirty;
        private bool _attached;
        private Window? _owner;

        public AutoSaveSubscription(DataGrid grid)
        {
            _grid = grid;
            _timer = new DispatcherTimer(DispatcherPriority.Background, grid.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _timer.Tick += (_, _) => Save();
            grid.Loaded += OnLoaded;
            grid.Unloaded += OnUnloaded;
            OnLoaded(grid, new RoutedEventArgs());
        }

        public void Save()
        {
            _timer.Stop();
            if (!_dirty)
                return;
            try
            {
                Persist(_grid);
                _dirty = false;
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Cannot save table column layout: {0}", ex.Message);
            }
        }

        private void Changed(object? sender, EventArgs args)
        {
            _dirty = true;
            _timer.Stop();
            _timer.Start();
        }

        private void Reordered(object? sender, DataGridColumnEventArgs args) => Changed(sender, args);
        private void OnClosing(object? sender, CancelEventArgs args) => Save();

        private void ObserveColumns()
        {
            foreach (var column in _grid.Columns)
            {
                if (!_observed.Add(column))
                    continue;
                foreach (var descriptor in _descriptors)
                    descriptor.AddValueChanged(column, Changed);
            }
        }

        private void ColumnsChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            foreach (var column in _observed.Where(column => !_grid.Columns.Contains(column)).ToArray())
            {
                foreach (var descriptor in _descriptors)
                    descriptor.RemoveValueChanged(column, Changed);
                _observed.Remove(column);
            }
            ObserveColumns();
        }

        private void OnLoaded(object? sender, RoutedEventArgs args)
        {
            if (_attached)
                return;
            _attached = true;
            ObserveColumns();
            _grid.ColumnReordered += Reordered;
            ((INotifyCollectionChanged)_grid.Columns).CollectionChanged += ColumnsChanged;
            _owner = Window.GetWindow(_grid);
            if (_owner is not null)
                _owner.Closing += OnClosing;
        }

        private void OnUnloaded(object? sender, RoutedEventArgs args)
        {
            Save();
            foreach (var column in _observed)
                foreach (var descriptor in _descriptors)
                    descriptor.RemoveValueChanged(column, Changed);
            _observed.Clear();
            _grid.ColumnReordered -= Reordered;
            ((INotifyCollectionChanged)_grid.Columns).CollectionChanged -= ColumnsChanged;
            if (_owner is not null)
                _owner.Closing -= OnClosing;
            _owner = null;
            _attached = false;
        }
    }

    private static void ScheduleAutoFit(DataGrid grid)
    {
        void TryFit()
        {
            if (!grid.IsVisible && grid.Visibility != Visibility.Visible)
                return;

            FitColumns(grid);
        }

        grid.Dispatcher.BeginInvoke(TryFit, DispatcherPriority.ContextIdle);

        if (grid.Items is INotifyCollectionChanged incc)
        {
            NotifyCollectionChangedEventHandler? handler = null;
            handler = (_, _) =>
            {
                if (grid.Items.Count == 0)
                    return;

                incc.CollectionChanged -= handler;
                grid.Dispatcher.BeginInvoke(TryFit, DispatcherPriority.ContextIdle);
            };
            incc.CollectionChanged += handler;
        }
    }

    private static void AttachHeaderMenu(DataGrid grid)
    {
        if ((bool)grid.GetValue(HeaderMenuAttachedProperty))
            return;

        var menu = new ContextMenu();
        var showColumns = new MenuItem { Header = "Cột hiển thị" };
        showColumns.Click += (_, _) => ShowColumnVisibilityDialog(grid);

        var fitColumns = new MenuItem { Header = "Dãn cột" };
        fitColumns.Click += (_, _) => FitColumnsAndPersist(grid);

        menu.Items.Add(showColumns);
        menu.Items.Add(fitColumns);

        var basedOn = grid.ColumnHeaderStyle ?? grid.TryFindResource(typeof(DataGridColumnHeader)) as Style;
        var style = basedOn is null
            ? new Style(typeof(DataGridColumnHeader))
            : new Style(typeof(DataGridColumnHeader), basedOn);
        style.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, menu));
        grid.ColumnHeaderStyle = style;

        grid.SetValue(HeaderMenuAttachedProperty, true);
    }
}
