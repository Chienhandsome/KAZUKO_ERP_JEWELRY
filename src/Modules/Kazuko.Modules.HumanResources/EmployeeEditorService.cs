using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.UserManagement;
using Kazuko.Shared.UI.Controls;

namespace Kazuko.Modules.HumanResources;

public sealed class EmployeeEditorService : IEmployeeEditorService
{
    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IAuthorizationService _authorization;

    public EmployeeEditorService(IHumanResourcesRepository repository, ICurrentSessionAccessor sessionAccessor,
        IAuthorizationService authorization)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _authorization = authorization;
    }

    public EmployeeEditResult? ShowCreate() => Show(null);

    public EmployeeEditResult? ShowEdit(long employeeId) => Show(employeeId);

    public EmployeeCatalogResult ShowCatalog(bool selectProductionWorker = false, long? selectedId = null)
    {
        var dialog = new CatalogDialog
        {
            Title = selectProductionWorker ? "Danh mục thợ sản xuất" : "Danh mục nhân viên",
            Owner = ResolveOwner()
        };
        var viewModel = new EmployeeCatalogViewModel(_repository, _sessionAccessor, _authorization,
            id => Show(id, dialog), selectProductionWorker,
            message => MessageBox.Show(dialog, message, "Ngừng hoạt động", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        dialog.DataContext = viewModel;
        dialog.Loaded += async (_, _) => await viewModel.LoadAsync(selectedId);
        dialog.Closing += (_, args) => args.Cancel = viewModel.IsBusy;
        viewModel.EmployeeChosen += () => dialog.DialogResult = true;
        dialog.ShowDialog();
        return new EmployeeCatalogResult(viewModel.ChosenEmployee, viewModel.HasChanges);
    }

    private EmployeeEditResult? Show(long? employeeId, Window? owner = null)
    {
        var viewModel = new EmployeeEditDialogViewModel(_repository, _sessionAccessor, employeeId);
        var dialog = new EmployeeEditDialog
        {
            Owner = owner ?? ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        EmployeeEditResult? result = null;
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
