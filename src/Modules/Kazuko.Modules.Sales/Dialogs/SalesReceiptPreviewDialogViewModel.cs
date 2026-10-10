using System.Diagnostics;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Printing;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales.Dialogs;

public sealed class SalesReceiptPreviewDialogViewModel : ObservableObject
{
    private readonly SalesReceiptPrintService _printService;
    private readonly AuthenticatedSession _session;
    private string _pdfPath;
    private SalesReceiptOrientationOption _selectedOrientation;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private bool _suppressOrientationReload;

    public SalesReceiptPreviewDialogViewModel(
        SalesReceiptDto receipt,
        string pdfPath,
        SalesReceiptPrintService printService,
        AuthenticatedSession session,
        SalesReceiptOrientation orientation)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(printService);
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(pdfPath))
            throw new ArgumentException("PDF path is required.", nameof(pdfPath));

        Receipt = receipt;
        _pdfPath = pdfPath;
        _printService = printService;
        _session = session;
        DocumentCode = receipt.Checkout.DocumentCode;

        OrientationOptions =
        [
            new SalesReceiptOrientationOption(SalesReceiptOrientation.Landscape, "A5 ngang"),
            new SalesReceiptOrientationOption(SalesReceiptOrientation.Portrait, "A5 dọc")
        ];
        _suppressOrientationReload = true;
        _selectedOrientation = OrientationOptions.First(x => x.Value == orientation);
        _suppressOrientationReload = false;

        PrintCommand = new AsyncRelayCommand(PrintAsync, () => !IsBusy);
        OpenPdfCommand = new RelayCommand(OpenPdf, () => !IsBusy);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
    }

    public SalesReceiptDto Receipt { get; }
    public string DocumentCode { get; }
    public IReadOnlyList<SalesReceiptOrientationOption> OrientationOptions { get; }

    public string PdfPath
    {
        get => _pdfPath;
        private set => SetProperty(ref _pdfPath, value);
    }

    public SalesReceiptOrientationOption SelectedOrientation
    {
        get => _selectedOrientation;
        set
        {
            if (value is null || !SetProperty(ref _selectedOrientation, value))
                return;
            if (!_suppressOrientationReload)
                _ = ChangeOrientationAsync(value);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;
            ((AsyncRelayCommand)PrintCommand).NotifyCanExecuteChanged();
            ((RelayCommand)OpenPdfCommand).NotifyCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand PrintCommand { get; }
    public ICommand OpenPdfCommand { get; }
    public ICommand CloseCommand { get; }

    /// <summary>Dialog hooks PrintDocument fit-print here; fallback PrintFitted.</summary>
    public Func<Task>? PrintViaPreviewAsync { get; set; }

    public Func<string, Task>? ReloadPdfAsync { get; set; }

    public event EventHandler<bool>? CloseRequested;

    private async Task ChangeOrientationAsync(SalesReceiptOrientationOption? option)
    {
        if (option is null || IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Đang tạo PDF…";
        try
        {
            SalesReceiptPrintPreferences.Save(option.Value);
            var previous = PdfPath;
            var next = await _printService.ExportToTempPdfAsync(Receipt, _session, option.Value);
            PdfPath = next;
            if (ReloadPdfAsync is not null)
                await ReloadPdfAsync(next);
            SalesReceiptPrintService.TryDeleteTempPdf(previous);
            StatusMessage = $"Đã chuyển sang {option.Label}.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void PrintFitted()
    {
        try
        {
            _printService.PrintPdfFitted(PdfPath, SelectedOrientation.Value);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "In phiếu bán hàng",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private async Task PrintAsync()
    {
        try
        {
            if (PrintViaPreviewAsync is not null)
            {
                await PrintViaPreviewAsync();
                return;
            }

            PrintFitted();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "In phiếu bán hàng",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OpenPdf()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PdfPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                "Mở PDF",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }
}

public sealed record SalesReceiptOrientationOption(SalesReceiptOrientation Value, string Label)
{
    public override string ToString() => Label;
}
