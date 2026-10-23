using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Customers;

public sealed class CustomerEditorService : ICustomerEditorService
{
    private readonly ICustomerRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IAuthorizationService? _authorization;

    public CustomerEditorService(ICustomerRepository repository, ICurrentSessionAccessor sessionAccessor,
        IAuthorizationService? authorization = null)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _authorization = authorization;
    }

    public CustomerEditResult? ShowCreate() => Show(null);

    public CustomerEditResult? ShowEdit(long customerId) => Show(customerId);

    private CustomerEditResult? Show(long? customerId, CitizenIdentityScanData? initialScan = null)
    {
        var viewModel = new CustomerEditDialogViewModel(_repository, _sessionAccessor, customerId);
        var dialog = new CustomerEditDialog
        {
            Owner = ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        CustomerEditResult? result = null;
        void OpenScan(CitizenIdentityScanData? data = null)
        {
            var session = _sessionAccessor.Current;
            if (session is null || !viewModel.IsFormEnabled) return;
            var scanner = new CitizenScanDialogViewModel(_repository, session, customerId, viewModel.ScanSnapshot,
                _authorization?.Current.Allows("CAT_CUSTOMERS", PermissionAction.Edit) == true);
            var scanDialog = new CitizenScanDialog { Owner = dialog, DataContext = scanner };
            scanner.RequestClose += () => scanDialog.DialogResult = scanner.Outcome is not null;
            if (data is not null) scanDialog.Loaded += async (_, _) => await scanner.LoadDataAsync(data);
            scanDialog.ShowDialog();
            if (scanner.Outcome is not { } outcome) return;
            if (outcome.Existing is { } match)
            {
                if (outcome.EditExisting)
                {
                    var edited = Show(match.Id, outcome.Data);
                    if (edited is not null) viewModel.CompleteWithExisting(new(edited.Id, edited.Code,
                        edited.Name, edited.Phone, edited.Address, outcome.Data.Number, edited.IsActive, edited.IsWalkIn));
                }
                else viewModel.CompleteWithExisting(match);
            }
            else if (viewModel.ApplyCitizenData(outcome.Data, confirmReplacement: true)) dialog.FocusPhoneIfEmpty();
        }
        viewModel.ScanRequested += () => OpenScan();
        if (initialScan is not null)
            dialog.Loaded += async (_, _) => { await viewModel.Initialization; if (dialog.IsLoaded) OpenScan(initialScan); };
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
