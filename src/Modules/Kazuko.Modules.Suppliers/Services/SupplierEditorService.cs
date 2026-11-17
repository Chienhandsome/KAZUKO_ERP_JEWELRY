using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Suppliers;
using Kazuko.Core.UserManagement;
using Kazuko.Shared.UI.Controls;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierEditorService : ISupplierEditorService
{
    private readonly ISupplierRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IBusinessTaxLookupService _taxLookup;
    private readonly IAuthorizationService _authorization;

    public SupplierEditorService(ISupplierRepository repository, ICurrentSessionAccessor sessionAccessor,
        IBusinessTaxLookupService taxLookup, IAuthorizationService authorization)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _taxLookup = taxLookup;
        _authorization = authorization;
    }

    public SupplierEditResult? ShowCreate() => Show(null);

    public SupplierEditResult? ShowEdit(long supplierId) => Show(supplierId);

    public SupplierCatalogResult ShowCatalog(bool selectSupplier = false, long? selectedId = null)
    {
        var dialog = new CatalogDialog { Title = "Danh mục nhà cung cấp", Owner = ResolveOwner() };
        var viewModel = new SupplierCatalogViewModel(_repository, _sessionAccessor, _authorization,
            id => Show(id, dialog), selectSupplier,
            message => MessageBox.Show(dialog, message, "Xóa nhà cung cấp", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        dialog.DataContext = viewModel;
        dialog.Closing += (_, e) => e.Cancel = viewModel.IsBusy;
        dialog.Loaded += async (_, _) => await viewModel.LoadAsync(selectedId);
        viewModel.SupplierChosen += () => dialog.DialogResult = true;
        dialog.ShowDialog();
        return new SupplierCatalogResult(viewModel.ChosenSupplier, viewModel.HasChanges);
    }

    private SupplierEditResult? Show(long? supplierId, Window? owner = null)
    {
        var viewModel = new SupplierEditDialogViewModel(_repository, _sessionAccessor, supplierId, _taxLookup);
        var dialog = new SupplierEditDialog
        {
            Owner = owner ?? ResolveOwner(),
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
