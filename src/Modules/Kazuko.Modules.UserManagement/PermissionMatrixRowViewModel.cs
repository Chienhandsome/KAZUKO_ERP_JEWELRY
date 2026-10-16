using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.UserManagement;

/// <summary>
/// Một dòng ma trận phân quyền trên UI. Áp quy tắc kế thừa ngay khi người dùng
/// tích/bỏ tích (xem PermissionInheritanceRules):
/// - Thêm/Xóa → tự check Sửa + Xem; Sửa → tự check Xem.
/// - Bỏ Xem → bỏ Thêm/Sửa/Xóa. Tất cả → check hết 4. Bị khóa → disable + bỏ hết.
/// </summary>
public sealed class PermissionMatrixRowViewModel : ObservableObject
{
    private bool _isLocked;
    private bool _canView;
    private bool _canAdd;
    private bool _canEdit;
    private bool _canDelete;

    public PermissionMatrixRowViewModel(string functionCode, string functionName, string moduleGroup)
    {
        FunctionCode = functionCode;
        FunctionName = functionName;
        ModuleGroup = moduleGroup;
    }

    public string FunctionCode { get; }
    public string FunctionName { get; }
    public string ModuleGroup { get; }

    /// <summary>Các ô check bị vô hiệu hóa khi chức năng đang "Bị khóa".</summary>
    public bool IsCheckEnabled => !IsLocked;

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (!SetProperty(ref _isLocked, value))
                return;
            if (value)
                SetAllChecks(false, false, false, false);
            OnPropertyChanged(nameof(IsCheckEnabled));
        }
    }

    public bool CanView
    {
        get => _canView;
        set
        {
            if (!SetProperty(ref _canView, value))
                return;
            if (!value)
                SetAllChecks(false, false, false, false);
            OnPropertyChanged(nameof(All));
        }
    }

    public bool CanAdd
    {
        get => _canAdd;
        set
        {
            if (!SetProperty(ref _canAdd, value))
                return;
            if (value)
            {
                SetCheck(ref _canView, true, nameof(CanView));
                SetCheck(ref _canEdit, true, nameof(CanEdit));
            }
            OnPropertyChanged(nameof(All));
        }
    }

    public bool CanEdit
    {
        get => _canEdit;
        set
        {
            if (!SetProperty(ref _canEdit, value))
                return;
            if (value)
                SetCheck(ref _canView, true, nameof(CanView));
            OnPropertyChanged(nameof(All));
        }
    }

    public bool CanDelete
    {
        get => _canDelete;
        set
        {
            if (!SetProperty(ref _canDelete, value))
                return;
            if (value)
            {
                SetCheck(ref _canView, true, nameof(CanView));
                SetCheck(ref _canEdit, true, nameof(CanEdit));
            }
            OnPropertyChanged(nameof(All));
        }
    }

    public bool All
    {
        get => _canView && _canAdd && _canEdit && _canDelete;
        set
        {
            SetAllChecks(value, value, value, value);
            OnPropertyChanged(nameof(All));
        }
    }

    /// <summary>Gán trạng thái ban đầu từ DB (không kích hoạt kế thừa).</summary>
    public PermissionMatrixRowViewModel Initialize(
        bool canView, bool canAdd, bool canEdit, bool canDelete, bool isLocked)
    {
        _isLocked = isLocked;
        _canView = canView;
        _canAdd = canAdd;
        _canEdit = canEdit;
        _canDelete = canDelete;
        OnPropertyChanged(string.Empty);
        return this;
    }

    public PermissionSaveRow ToSaveRow() => new(
        FunctionCode, _canView, _canAdd, _canEdit, _canDelete, _isLocked);

    private void SetAllChecks(bool view, bool add, bool edit, bool delete)
    {
        SetCheck(ref _canView, view, nameof(CanView));
        SetCheck(ref _canAdd, add, nameof(CanAdd));
        SetCheck(ref _canEdit, edit, nameof(CanEdit));
        SetCheck(ref _canDelete, delete, nameof(CanDelete));
        OnPropertyChanged(nameof(All));
    }

    private void SetCheck(ref bool field, bool value, string propertyName) =>
        SetProperty(ref field, value, propertyName);

    public static IReadOnlyList<PermissionMatrixRowViewModel> BuildFrom(
        IReadOnlyList<GroupPermissionRow> saved)
    {
        var byCode = saved.ToDictionary(
            r => r.FunctionCode, StringComparer.OrdinalIgnoreCase);
        return SystemFunctionCatalog.All
            .Select(f =>
            {
                var row = byCode.TryGetValue(f.Code, out var s) ? s : null;
                return new PermissionMatrixRowViewModel(f.Code, f.Name, f.ModuleGroup)
                    .Initialize(
                        row?.CanView == true,
                        row?.CanAdd == true,
                        row?.CanEdit == true,
                        row?.CanDelete == true,
                        row?.IsLocked == true);
            })
            .ToArray();
    }
}
