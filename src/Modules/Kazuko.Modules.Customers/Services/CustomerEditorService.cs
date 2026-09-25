using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;

namespace Kazuko.Modules.Customers;

public sealed class CustomerEditorService : ICustomerEditorService
{
    private readonly ICustomerRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;

    public CustomerEditorService(ICustomerRepository repository, ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
    }

    public CustomerEditResult? ShowCreate() => Show(null);

    public CustomerEditResult? ShowEdit(long customerId) => Show(customerId);

    private CustomerEditResult? Show(long? customerId)
    {
        var viewModel = new CustomerEditDialogViewModel(_repository, _sessionAccessor, customerId);
        var dialog = new CustomerEditDialog
        {
            Owner = ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        CustomerEditResult? result = null;
        viewModel.RequestClose += value =>
        {
            result = value;
            if (dialog.IsLoaded)
                dialog.DialogResult = value is not null;
            else
                dialog.Close();
        };

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
