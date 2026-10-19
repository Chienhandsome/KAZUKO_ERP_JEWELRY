using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Windows.Input;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Warehouse;

/// <summary>Dialog chọn máy in tem + số bản in.</summary>
public sealed class LabelPrintDialogViewModel : ObservableObject
{
    private string? _selectedPrinter;
    private string _copiesText = "1";
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;

    public LabelPrintDialogViewModel(int itemCount, string? summaryText = null)
    {
        Printers = new ObservableCollection<string>();
        foreach (string printer in PrinterSettings.InstalledPrinters)
            Printers.Add(printer);

        var lastPrinter = LabelPrinterPreferences.LoadPrinterName();
        _selectedPrinter = !string.IsNullOrWhiteSpace(lastPrinter) && Printers.Contains(lastPrinter)
            ? lastPrinter
            : Printers.FirstOrDefault();

        SummaryText = summaryText ?? $"In tem cho {itemCount} sản phẩm đã chọn.";

        PrintCommand = new RelayCommand(Print, () => !string.IsNullOrWhiteSpace(SelectedPrinter));
        CancelCommand = new RelayCommand(() => DialogResult = false);
    }

    public ObservableCollection<string> Printers { get; }

    public string SummaryText { get; }

    public string? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (SetProperty(ref _selectedPrinter, value))
                ((RelayCommand)PrintCommand).NotifyCanExecuteChanged();
        }
    }

    public string CopiesText
    {
        get => _copiesText;
        set => SetProperty(ref _copiesText, value);
    }

    public int Copies
    {
        get
        {
            if (int.TryParse(CopiesText, out var copies) && copies > 0 && copies <= 999)
                return copies;
            return 1;
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public ICommand PrintCommand { get; }
    public ICommand CancelCommand { get; }

    private void Print()
    {
        if (string.IsNullOrWhiteSpace(SelectedPrinter))
        {
            ErrorMessage = "Vui lòng chọn máy in.";
            return;
        }

        if (!int.TryParse(CopiesText, out var copies) || copies <= 0 || copies > 999)
        {
            ErrorMessage = "Số bản in phải từ 1 đến 999.";
            return;
        }

        LabelPrinterPreferences.SavePrinterName(SelectedPrinter);
        DialogResult = true;
    }
}
