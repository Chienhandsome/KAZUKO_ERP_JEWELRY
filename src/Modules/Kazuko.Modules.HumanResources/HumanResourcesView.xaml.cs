using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Kazuko.Modules.HumanResources;

public partial class HumanResourcesView : UserControl
{
    private bool _attendanceDragging;

    public HumanResourcesView()
    {
        InitializeComponent();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1 && DataContext is HumanResourcesViewModel vm && vm.AddCommand.CanExecute(null))
        {
            vm.AddCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------
    // Chấm công: chọn ô bằng click / kéo thả chuột
    // ------------------------------------------------------------------

    private void AttendanceCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not AttendanceCellViewModel cell)
            return;
        if (DataContext is not HumanResourcesViewModel root)
            return;

        _attendanceDragging = true;
        border.CaptureMouse();
        var additive = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        root.Attendance.BeginDragSelect(cell, additive);
        e.Handled = true;
    }

    private void AttendanceGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_attendanceDragging || DataContext is not HumanResourcesViewModel root)
            return;

        var position = e.GetPosition(AttendanceGroupsControl);
        if (FindAttendanceCell(position) is { } cell)
            root.Attendance.UpdateDragSelect(cell);
    }

    private void AttendanceGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_attendanceDragging)
            return;

        _attendanceDragging = false;
        Mouse.Capture(null);
        if (DataContext is HumanResourcesViewModel root)
            root.Attendance.EndDragSelect();
    }

    private AttendanceCellViewModel? FindAttendanceCell(Point position)
    {
        var hit = AttendanceGroupsControl.InputHitTest(position) as DependencyObject;
        while (hit is not null)
        {
            if (hit is FrameworkElement { DataContext: AttendanceCellViewModel cell })
                return cell;
            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }

    private void AttendanceBodyScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        AttendanceHeaderScroller.ScrollToHorizontalOffset(e.HorizontalOffset);
    }
}
