using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierEditorService : ISupplierEditorService
{
    private readonly ISupplierRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IBusinessTaxLookupService _taxLookup;

    public SupplierEditorService(ISupplierRepository repository, ICurrentSessionAccessor sessionAccessor,
        IBusinessTaxLookupService taxLookup)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _taxLookup = taxLookup;
    }

    public SupplierEditResult? ShowCreate() => Show(null);

    public SupplierEditResult? ShowEdit(long supplierId) => Show(supplierId);

    private SupplierEditResult? Show(long? supplierId)
    {
        var viewModel = new SupplierEditDialogViewModel(_repository, _sessionAccessor, supplierId, _taxLookup);
        var dialog = new SupplierEditDialog
        {
            Owner = ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        SupplierEditResult? result = null;
        viewModel.RequestClose += value =>
        {
            result = value;
            if (dialog.IsLoaded)
                dialog.DialogResult = value is not null;
            else
                dialog.Close();
        };

        dialog.Closed += (_, _) => viewModel.CancelTaxLookup();
        dialog.ShowDialog();
        return result;
    }

    private static Window? ResolveOwner()
    {
        if (Application.Current?.Windows is null)
            return Application.Current?.MainWindow;

        foreach (Window window in Application.Current.Windows)
        {
            if (window.IsActive)
                return window;
        }

        return Application.Current.MainWindow;
    }
}
