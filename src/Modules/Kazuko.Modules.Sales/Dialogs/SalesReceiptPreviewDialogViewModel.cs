using System.Windows.Documents;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales.Dialogs;

public sealed class SalesReceiptPreviewDialogViewModel
{
    public SalesReceiptPreviewDialogViewModel(SalesReceiptDto receipt, FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(document);
        Receipt = receipt;
        Document = document;
        DocumentCode = receipt.Checkout.DocumentCode;
        PrintCommand = new RelayCommand(() => PrintRequested?.Invoke(this, EventArgs.Empty));
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, false));
    }

    public SalesReceiptDto Receipt { get; }
    public FlowDocument Document { get; }
    public string DocumentCode { get; }
    public ICommand PrintCommand { get; }
    public ICommand CloseCommand { get; }

    public event EventHandler? PrintRequested;
    public event EventHandler<bool>? CloseRequested;
}
