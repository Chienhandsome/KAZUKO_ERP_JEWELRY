using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.HumanResources;

/// <summary>Mở danh mục và form thêm ca làm việc trong module Nhân sự.</summary>
public interface IShiftEditorService
{
    /// <summary>Trả về ca vừa tạo, hoặc null nếu người dùng hủy.</summary>
    WorkShiftItem? ShowCreate();
    void ShowManage();
}

public sealed class ShiftEditorService : IShiftEditorService
{
    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IAuthorizationService? _permissions;

    public ShiftEditorService(IHumanResourcesRepository repository, ICurrentSessionAccessor sessionAccessor, IAuthorizationService? permissions = null)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _permissions = permissions;
    }

    public WorkShiftItem? ShowCreate()
    {
        var viewModel = new ShiftEditDialogViewModel(_repository, _sessionAccessor, permissions: _permissions);
        var dialog = new ShiftEditDialog
        {
            Owner = ResolveOwner(),
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        WorkShiftItem? result = null;
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

    public void ShowManage()
    {
        var viewModel = new WorkShiftCatalogViewModel(_repository, _sessionAccessor, _permissions);
        viewModel.RequestCreate = () => Task.FromResult(ShowCreate());
        new WorkShiftCatalogDialog(viewModel) { Owner = ResolveOwner() }.ShowDialog();
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
