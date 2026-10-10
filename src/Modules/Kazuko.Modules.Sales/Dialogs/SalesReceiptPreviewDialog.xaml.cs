using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Sales.Dialogs;

public partial class SalesReceiptPreviewDialog : Window
{
    private readonly SalesReceiptPreviewDialogViewModel _viewModel;

    public SalesReceiptPreviewDialog(SalesReceiptPreviewDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        // In bằng PrintDocument (A5 + fit margins + center) giống setting WPS — không dùng WebView2 PrintAsync.
        viewModel.PrintViaPreviewAsync = PrintFittedAsync;
        viewModel.ReloadPdfAsync = LoadPdfAsync;
        viewModel.CloseRequested += (_, result) =>
        {
            DialogResult = result;
            Close();
        };
        Loaded += async (_, _) => await LoadPdfAsync(viewModel.PdfPath);
    }

    private Task PrintFittedAsync()
    {
        _viewModel.PrintFitted();
        return Task.CompletedTask;
    }

    private async Task LoadPdfAsync(string pdfPath)
    {
        FallbackText.Visibility = Visibility.Visible;
        try
        {
            await PdfViewer.EnsureCoreWebView2Async();
            PdfViewer.CoreWebView2.Navigate(new Uri(pdfPath).AbsoluteUri);
            FallbackText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            FallbackText.Text =
                "Không mở được xem trước PDF trong app.\n"
                + "Cần Microsoft Edge WebView2 Runtime.\n"
                + "Dùng nút «Mở PDF» hoặc «In».\n\n"
                + ex.Message;
            FallbackText.Visibility = Visibility.Visible;
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }
}
