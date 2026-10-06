using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales.Dialogs;

public partial class SalesReceiptPreviewDialog : Window
{
    public SalesReceiptPreviewDialog(SalesReceiptPreviewDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewViewer.Document = viewModel.Document;
        viewModel.CloseRequested += (_, result) =>
        {
            DialogResult = result;
            Close();
        };
        viewModel.PrintRequested += OnPrintRequested;
    }

    private void OnPrintRequested(object? sender, EventArgs e)
    {
        if (DataContext is not SalesReceiptPreviewDialogViewModel vm)
            return;

        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            var paginator = ((IDocumentPaginatorSource)vm.Document).DocumentPaginator;
            paginator.PageSize = new Size(
                SalesReceiptPrintService.A4WidthDip,
                SalesReceiptPrintService.A4HeightDip);
            dialog.PrintDocument(paginator, $"Phiếu {vm.DocumentCode}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "In phiếu bán hàng",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape
            && DataContext is SalesReceiptPreviewDialogViewModel vm)
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }
}
