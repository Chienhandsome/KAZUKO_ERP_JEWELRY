using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;

namespace Kazuko.Modules.HumanResources;

public sealed class EmployeeEditorService : IEmployeeEditorService
{
    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;

    public EmployeeEditorService(IHumanResourcesRepository repository, ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
    }

    public EmployeeEditResult? ShowCreate() => Show(null);

    public EmployeeEditResult? ShowEdit(long employeeId) => Show(employeeId);

    private EmployeeEditResult? Show(long? employeeId)
    {
        var viewModel = new EmployeeEditDialogViewModel(_repository, _sessionAccessor, employeeId);
        var dialog = new EmployeeEditDialog
        {
            Owner = ResolveOwner(),
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
