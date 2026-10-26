using System.Windows;
using Kazuko.Core.Finance;

namespace Kazuko.Modules.Finance;

public partial class VoucherCategoryEditorDialog : Window
{
    public VoucherCategory? SavedCategory { get; private set; }
    public VoucherCategoryEditorDialog(VoucherCategoryViewModel vm)
    {
        InitializeComponent(); DataContext = vm;
        Title = vm.CreateTitle;
        Heading.Text = Title;
        Loaded += (_, _) => CodeInput.Focus();
        void OnSaved(VoucherCategory category) { SavedCategory = category; DialogResult = true; }
        vm.Saved += OnSaved;
        Closed += (_, _) => vm.Saved -= OnSaved;
        Closing += (_, e) => { if (vm.IsBusy && SavedCategory is null) e.Cancel = true; };
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
