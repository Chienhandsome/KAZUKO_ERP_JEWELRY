using System.Windows;
using System.Windows.Controls;

namespace Kazuko.Modules.Reports.Shared;

public partial class ReportWorkspaceView : UserControl
{
    public static readonly DependencyProperty FilterContentProperty =
        DependencyProperty.Register(
            nameof(FilterContent),
            typeof(object),
            typeof(ReportWorkspaceView),
            new PropertyMetadata(null, OnFilterContentChanged));

    public static readonly DependencyProperty EmptyMessageProperty =
        DependencyProperty.Register(
            nameof(EmptyMessage),
            typeof(string),
            typeof(ReportWorkspaceView),
            new PropertyMetadata("Không có dữ liệu."));

    public ReportWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    public object? FilterContent
    {
        get => GetValue(FilterContentProperty);
        set => SetValue(FilterContentProperty, value);
    }

    public string EmptyMessage
    {
        get => (string)GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    private ReportWorkspaceViewModel? _workspace;

    private static void OnFilterContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ReportWorkspaceView view)
            view.SyncFilterDataContext();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachWorkspace();
        SyncFilterDataContext();
        if (e.NewValue is ReportWorkspaceViewModel workspace)
            AttachWorkspace(workspace);
    }

    private void SyncFilterDataContext()
    {
        if (FilterContent is FrameworkElement element)
            element.DataContext = DataContext;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachWorkspace();

    private void AttachWorkspace(ReportWorkspaceViewModel workspace)
    {
        _workspace = workspace;
        _workspace.PropertyChanged += Workspace_OnPropertyChanged;
        _ = NavigatePdfAsync(_workspace.PreviewPdfPath);
    }

    private void DetachWorkspace()
    {
        if (_workspace is null)
            return;
        _workspace.PropertyChanged -= Workspace_OnPropertyChanged;
        _workspace = null;
    }

    private void Workspace_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReportWorkspaceViewModel.PreviewPdfPath)
            or nameof(ReportWorkspaceViewModel.HasPreviewPdf))
        {
            _ = NavigatePdfAsync(_workspace?.PreviewPdfPath);
        }
    }

    private async Task NavigatePdfAsync(string? pdfPath)
    {
        try
        {
            if (PdfViewer is null)
                return;

            await PdfViewer.EnsureCoreWebView2Async();

            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                PdfViewer.CoreWebView2.NavigateToString(
                    "<html><body style='font-family:Segoe UI;color:#666;padding:24px'>Chưa có bản xem báo cáo.</body></html>");
                return;
            }

            PdfViewer.CoreWebView2.Navigate(new Uri(pdfPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
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
