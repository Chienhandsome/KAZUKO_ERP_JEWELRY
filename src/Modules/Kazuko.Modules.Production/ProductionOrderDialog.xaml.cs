using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Kazuko.Modules.Production;

public partial class ProductionOrderDialog : Window
{
    public ProductionOrderDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing+=(_,e)=>
        {
            if(DataContext is not ProductionOrderDialogViewModel vm)return;
            if(vm.IsBusy){e.Cancel=true;return;}
            if(vm.IsDraft&&MessageBox.Show("Đóng LSX? Thay đổi chưa bấm Lưu nháp sẽ bị bỏ.","Đóng lệnh",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProductionOrderDialogViewModel viewModel)
            viewModel.RequestClose += success => DialogResult = success;
    }

    private void WorkerItem_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBoxItem { DataContext: SelectableEmployeeViewModel employee } || !WorkerPicker.IsEnabled)
            return;
        WorkerPicker.SelectedItem = employee; // Keyboard highlight is independent of the checked workers.
        employee.IsSelected = !employee.IsSelected;
        e.Handled = true; // Suppress both single-item selection and a second toggle by CheckBox.
    }

    private void WorkerPicker_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (WorkerPicker.IsDropDownOpen && e.Key == Key.Escape)
        {
            WorkerPicker.IsDropDownOpen = false;
            e.Handled = true;
            return;
        }
        if (WorkerPicker.IsDropDownOpen && e.Key is Key.Space or Key.Enter
            && WorkerPicker.SelectedItem is SelectableEmployeeViewModel employee)
        {
            employee.IsSelected = !employee.IsSelected;
            e.Handled = true;
        }
    }

    private void WorkerItem_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBoxItem { DataContext: SelectableEmployeeViewModel } && WorkerPicker.IsEnabled)
            e.Handled = true; // Keep the multi-select list open after checking a worker.
    }

    private void LinesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ProductionOrderDialogViewModel viewModel)
            return;

        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is DataGridRow { Item: ProductionLineEditorViewModel row })
            {
                if (viewModel.EditLineCommand.CanExecute(row))
                    viewModel.EditLineCommand.Execute(row);
                e.Handled = true;
                return;
            }
            if (ReferenceEquals(element, sender))
                return;
        }
    }
}
