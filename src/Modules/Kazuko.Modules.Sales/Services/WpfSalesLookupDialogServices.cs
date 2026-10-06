using System.Windows;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Dialogs;

namespace Kazuko.Modules.Sales.Services;

public sealed class WpfSalesCustomerLookupDialogService : ISalesCustomerLookupDialogService
{
    public SalesCustomerOption? Show(
        Func<string, Task<IReadOnlyList<SalesCustomerOption>>> searchAsync,
        SalesCustomerOption? current)
    {
        ArgumentNullException.ThrowIfNull(searchAsync);
        var viewModel = new SalesCustomerLookupDialogViewModel(searchAsync, current);
        var dialog = new SalesCustomerLookupDialog(viewModel)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
                ?? Application.Current?.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }
}

public sealed class WpfSalesPriceOverrideDialogService : ISalesPriceOverrideDialogService
{
    public bool TryEdit(SellLineRowViewModel line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var viewModel = new SalesPriceOverrideDialogViewModel(
            line.ProductCode,
            line.ProductName,
            line.SourceGoldPricePerChi,
            line.SourceLaborAmountVnd,
            line.SourceStoneAmountVnd,
            line.AppliedGoldPricePerChi,
            line.AppliedLaborAmountVnd,
            line.AppliedStoneAmountVnd,
            line.OverrideReason);
        var dialog = new SalesPriceOverrideDialog(viewModel)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
                ?? Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true || !viewModel.Accepted)
            return false;

        line.AppliedGoldPricePerChi = viewModel.GoldPrice;
        line.AppliedLaborAmountVnd = viewModel.LaborAmount;
        line.AppliedStoneAmountVnd = viewModel.StoneAmount;
        line.OverrideReason = string.IsNullOrWhiteSpace(viewModel.OverrideReason)
            ? null
            : viewModel.OverrideReason.Trim();
        return true;
    }
}
