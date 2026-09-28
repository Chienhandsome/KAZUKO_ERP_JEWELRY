using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Kazuko.Shared.UI.Behaviors;

/// <summary>
/// Attaches a right-click row context menu (Sửa / Xóa / đổi trạng thái) to a DataGrid.
/// </summary>
public static class DataGridRowActions
{
    public static readonly DependencyProperty EditCommandProperty =
        DependencyProperty.RegisterAttached(
            "EditCommand",
            typeof(ICommand),
            typeof(DataGridRowActions),
            new PropertyMetadata(null, OnCommandsChanged));

    public static readonly DependencyProperty DeleteCommandProperty =
        DependencyProperty.RegisterAttached(
            "DeleteCommand",
            typeof(ICommand),
            typeof(DataGridRowActions),
            new PropertyMetadata(null, OnCommandsChanged));

    public static readonly DependencyProperty ToggleActiveCommandProperty =
        DependencyProperty.RegisterAttached(
            "ToggleActiveCommand",
            typeof(ICommand),
            typeof(DataGridRowActions),
            new PropertyMetadata(null, OnCommandsChanged));

    public static readonly DependencyProperty ToggleHeaderPathProperty =
        DependencyProperty.RegisterAttached(
            "ToggleHeaderPath",
            typeof(string),
            typeof(DataGridRowActions),
            new PropertyMetadata("ToggleActiveLabel"));

    public static readonly DependencyProperty IsActivePathProperty =
        DependencyProperty.RegisterAttached(
            "IsActivePath",
            typeof(string),
            typeof(DataGridRowActions),
            new PropertyMetadata("IsActive"));

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsHooked",
            typeof(bool),
            typeof(DataGridRowActions),
            new PropertyMetadata(false));

    public static void SetEditCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(EditCommandProperty, value);

    public static ICommand? GetEditCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(EditCommandProperty);

    public static void SetDeleteCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(DeleteCommandProperty, value);

    public static ICommand? GetDeleteCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(DeleteCommandProperty);

    public static void SetToggleActiveCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ToggleActiveCommandProperty, value);

    public static ICommand? GetToggleActiveCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ToggleActiveCommandProperty);

    public static void SetToggleHeaderPath(DependencyObject element, string? value) =>
        element.SetValue(ToggleHeaderPathProperty, value);

    public static string GetToggleHeaderPath(DependencyObject element) =>
        (string)element.GetValue(ToggleHeaderPathProperty);

    public static void SetIsActivePath(DependencyObject element, string? value) =>
        element.SetValue(IsActivePathProperty, value);

    public static string GetIsActivePath(DependencyObject element) =>
        (string)element.GetValue(IsActivePathProperty);

    private static void OnCommandsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
            return;

        if ((bool)grid.GetValue(IsHookedProperty))
            return;

        grid.SetValue(IsHookedProperty, true);
        grid.PreviewMouseRightButtonUp += OnPreviewMouseRightButtonUp;
    }

    private static void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.Handled)
            return;

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is null || ReferenceEquals(row.Item, CollectionView.NewItemPlaceholder))
            return;

        // Don't steal clicks from nested interactive controls (buttons in action column).
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
            return;

        grid.SelectedItem = row.Item;
        row.Focus();

        var menu = BuildMenu(grid, row.Item);
        if (menu.Items.Count == 0)
            return;

        menu.PlacementTarget = row;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static ContextMenu BuildMenu(DataGrid grid, object rowItem)
    {
        var menu = new ContextMenu();

        var edit = GetEditCommand(grid);
        if (edit is not null)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "Sửa",
                Command = edit,
                CommandParameter = rowItem
            });
        }

        var delete = GetDeleteCommand(grid);
        if (delete is not null)
        {
            var deleteItem = new MenuItem
            {
                Header = "Xóa",
                Command = delete,
                CommandParameter = rowItem
            };
            var isActive = ReadBool(rowItem, GetIsActivePath(grid));
            deleteItem.IsEnabled = isActive == true && delete.CanExecute(rowItem);
            menu.Items.Add(deleteItem);
        }

        var toggle = GetToggleActiveCommand(grid);
        if (toggle is not null)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());

            var header = ReadString(rowItem, GetToggleHeaderPath(grid)) ?? "Đổi trạng thái";
            menu.Items.Add(new MenuItem
            {
                Header = header,
                Command = toggle,
                CommandParameter = rowItem
            });
        }

        return menu;
    }

    private static bool? ReadBool(object item, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var value = item.GetType().GetProperty(path, BindingFlags.Instance | BindingFlags.Public)?.GetValue(item);
        return value is bool b ? b : null;
    }

    private static string? ReadString(object item, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return item.GetType().GetProperty(path, BindingFlags.Instance | BindingFlags.Public)?.GetValue(item)?.ToString();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
