using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;

namespace Kazuko.Modules.HumanResources;

/// <summary>Mở dialog thêm ca làm việc (dùng nội bộ module Nhân sự).</summary>
public interface IShiftEditorService
{
    /// <summary>Trả về ca vừa tạo, hoặc null nếu người dùng hủy.</summary>
    WorkShiftItem? ShowCreate();
}

public sealed class ShiftEditorService : IShiftEditorService
{
    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;

    public ShiftEditorService(IHumanResourcesRepository repository, ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
    }

    public WorkShiftItem? ShowCreate()
    {
        var viewModel = new ShiftEditDialogViewModel(_repository, _sessionAccessor);
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
