using System.Windows;

namespace Kazuko.Modules.HumanResources;

public partial class WorkShiftCatalogDialog : Window
{
    public WorkShiftCatalogDialog(WorkShiftCatalogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ConfirmDelete = item => MessageBox.Show(this, $"Xóa ca \"{item.Name}\"?\nChỉ xóa được ca chưa có dữ liệu chấm công.",
            "Xóa ca làm việc", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        Loaded += async (_, _) => await viewModel.LoadAsync();
        Closing += (_, e) => { if (viewModel.IsBusy) e.Cancel = true; };
        Closed += (_, _) => { viewModel.RequestCreate = null; viewModel.ConfirmDelete = null; };
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
