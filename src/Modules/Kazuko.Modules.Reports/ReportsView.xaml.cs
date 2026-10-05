using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Kazuko.Modules.Reports;

public partial class ReportsView : UserControl
{
    private JewelryOnHandReportViewModel? _jewelryVm;

    public ReportsView() => InitializeComponent();

    private void ReportTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ReportsViewModel viewModel)
            viewModel.SelectedNode = e.NewValue as ReportTreeNode;
    }

    private void ReportsView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachJewelryVm();
        if (e.NewValue is ReportsViewModel reportsVm)
            AttachJewelryVm(reportsVm.JewelryOnHand);
    }

    private void ReportsView_OnUnloaded(object sender, RoutedEventArgs e) => DetachJewelryVm();

    private void AttachJewelryVm(JewelryOnHandReportViewModel jewelryVm)
    {
        _jewelryVm = jewelryVm;
        _jewelryVm.PropertyChanged += JewelryVm_OnPropertyChanged;
        _ = NavigatePdfAsync(_jewelryVm.PreviewPdfPath);
    }

    private void DetachJewelryVm()
    {
        if (_jewelryVm is null)
            return;
        _jewelryVm.PropertyChanged -= JewelryVm_OnPropertyChanged;
        _jewelryVm = null;
    }

    private void JewelryVm_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JewelryOnHandReportViewModel.PreviewPdfPath)
            or nameof(JewelryOnHandReportViewModel.HasPreviewPdf))
        {
            _ = NavigatePdfAsync(_jewelryVm?.PreviewPdfPath);
        }
    }

    private async Task NavigatePdfAsync(string? pdfPath)
    {
        try
        {
            if (ReportPdfViewer is null)
                return;

            await ReportPdfViewer.EnsureCoreWebView2Async();

            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                ReportPdfViewer.CoreWebView2.NavigateToString(
                    "<html><body style='font-family:Segoe UI;color:#666;padding:24px'>Chưa có bản xem báo cáo.</body></html>");
                return;
            }

            var uri = new Uri(pdfPath).AbsoluteUri;
            ReportPdfViewer.CoreWebView2.Navigate(uri);
        }
        catch (Exception ex)
        {
            // WebView2 Runtime chưa cài hoặc lỗi điều hướng PDF
            MessageBox.Show(
                Window.GetWindow(this),
                "Không hiển thị được bản xem báo cáo trong ứng dụng.\n"
                + "Cần Microsoft Edge WebView2 Runtime.\n\n"
                + ex.Message,
                "Báo cáo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
