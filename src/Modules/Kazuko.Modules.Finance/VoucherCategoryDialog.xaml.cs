using System.Windows;
namespace Kazuko.Modules.Finance;

public partial class VoucherCategoryDialog : Window
{
    public VoucherCategoryDialog(VoucherCategoryViewModel vm)
    {
        InitializeComponent(); DataContext = vm;
        vm.ConfirmDelete = item => MessageBox.Show(this, $"Xóa loại \"{item.Name}\"?\nChỉ xóa được loại chưa có phiếu sử dụng.",
            "Xóa loại thu/chi", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        Loaded += async (_, _) => await vm.LoadAsync();
        vm.RequestCreate = async () =>
        {
            var editorVm = vm.CreateNewEditor();
            var editor = new VoucherCategoryEditorDialog(editorVm) { Owner = this };
            if (editor.ShowDialog() == true && editor.SavedCategory is { } category)
                await vm.ReloadAndSelectAsync(category.Id);
        };
        Closed += (_, _) => { vm.RequestCreate = null; vm.ConfirmDelete = null; };
        Closing += (_, e) => { if (vm.IsBusy) e.Cancel = true; };
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
