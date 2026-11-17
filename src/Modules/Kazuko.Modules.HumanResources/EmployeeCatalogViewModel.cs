using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.HumanResources;

internal sealed class EmployeeCatalogViewModel : ObservableObject
{
    private const string Permission = "CAT_EMPLOYEES";
    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _session;
    private readonly IAuthorizationService _authorization;
    private readonly Func<long?, EmployeeEditResult?> _openEditor;
    private readonly Func<string, bool> _confirm;
    private IReadOnlyList<EmployeeCatalogRow> _allEmployees = [];
    private IReadOnlyList<EmployeeCatalogRow> _rows = [];
    private EmployeeCatalogRow? _selected;
    private string _query = string.Empty;
    private string _error = string.Empty;
    private bool _isBusy;

    public EmployeeCatalogViewModel(IHumanResourcesRepository repository, ICurrentSessionAccessor session,
        IAuthorizationService authorization, Func<long?, EmployeeEditResult?> openEditor, bool selectEmployee,
        Func<string, bool> confirm)
    {
        _repository = repository;
        _session = session;
        _authorization = authorization;
        _openEditor = openEditor;
        _confirm = confirm;
        CanChoose = selectEmployee;
        AddCommand = new AsyncRelayCommand(() => OpenEditorAsync(null), () => Can(PermissionAction.Add));
        EditCommand = new AsyncRelayCommand(() => OpenEditorAsync(Selected?.Id),
            () => Selected is not null && Can(PermissionAction.Edit));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync,
            () => Selected is { IsActive: true } && Can(PermissionAction.Delete));
        ChooseCommand = new RelayCommand(Choose,
            () => CanChoose && Selected is { IsActive: true } && Can(PermissionAction.View));
    }

    public bool CanChoose { get; }
    public string SearchHint => "Tìm theo tên, mã hoặc số điện thoại";
    public string DeleteHint => "Ngừng hoạt động với nhân viên";
    public string? SelectedGroupName => null;
    public string SelectedContext => EmployeeStatusCodes.ToDisplay(Selected?.StatusCode);
    public bool HasChanges { get; private set; }
    public EmployeeEditResult? ChosenEmployee { get; private set; }
    public event Action? EmployeeChosen;

    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand ChooseCommand { get; }

    public IReadOnlyList<EmployeeCatalogRow> Rows
    {
        get => _rows;
        private set => SetProperty(ref _rows, value);
    }

    public EmployeeCatalogRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(SelectedGroupName));
                OnPropertyChanged(nameof(SelectedContext));
                UpdateCommands();
            }
        }
    }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
                ApplyFilter(Selected?.Id);
        }
    }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                UpdateCommands();
        }
    }

    public async Task LoadAsync(long? selectedId)
    {
        IsBusy = true;
        Error = string.Empty;
        try
        {
            await ReloadAsync(selectedId);
        }
        catch (Exception)
        {
            _allEmployees = [];
            ApplyFilter(null);
            Error = "Không thể tải danh mục nhân viên. Vui lòng đóng và mở lại danh mục.";
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync(long? selectedId)
    {
        await _authorization.DemandAsync([new(Permission)]);
        if (_session.Current is null)
            throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

        var employees = await _repository.SearchAsync(new EmployeeSearchFilter(string.Empty, string.Empty));
        _allEmployees = employees.Where(employee => !CanChoose ||
            (employee.IsActive && employee.StatusCode == EmployeeStatusCodes.Working))
            .Select(employee => new EmployeeCatalogRow(employee.Id, employee.Code, employee.FullName,
                employee.Phone, employee.Email, employee.StatusCode, employee.IsActive)).ToArray();
        ApplyFilter(selectedId);
    }

    private void ApplyFilter(long? selectedId)
    {
        var query = Query.Trim();
        var employees = query.Length == 0 ? _allEmployees : _allEmployees.Where(row =>
            row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Code.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Phone.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Email.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        Rows = employees;
        Selected = employees.FirstOrDefault(row => row.Id == selectedId) ?? employees.FirstOrDefault();
    }

    private async Task OpenEditorAsync(long? id)
    {
        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _authorization.DemandAsync([new(Permission, id.HasValue ? PermissionAction.Edit : PermissionAction.Add)]);

            var result = _openEditor(id);
            if (result is null)
                return;

            HasChanges = true;
            Query = string.Empty;
            Selected = null;
            await ReloadAsync(result.Id);
        }
        catch (Exception)
        {
            Error = "Không thể cập nhật danh mục nhân viên. Vui lòng đóng và mở lại danh mục.";
        }
        finally { IsBusy = false; }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { IsActive: true } row || _session.Current is not { } session)
            return;
        if (!_confirm($"Ngừng hoạt động với {row.Code} – {row.Name}? Lịch sử giao dịch sẽ được giữ lại."))
            return;

        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _authorization.DemandAsync([new(Permission, PermissionAction.Delete)]);
            await _repository.SetActiveAsync(row.Id, false, session);
            HasChanges = true;
            _allEmployees = CanChoose
                ? _allEmployees.Where(employee => employee.Id != row.Id).ToArray()
                : _allEmployees.Select(employee => employee.Id == row.Id
                    ? employee with { IsActive = false, StatusCode = EmployeeStatusCodes.Terminated }
                    : employee).ToArray();
            ApplyFilter(row.Id);
        }
        catch (Exception)
        {
            Error = "Không thể ngừng giao dịch với nhân viên này.";
        }
        finally { IsBusy = false; }
    }

    private void Choose()
    {
        if (Selected is not { IsActive: true } row)
            return;
        ChosenEmployee = new EmployeeEditResult(row.Id, row.Code, row.Name, row.StatusCode, row.IsActive);
        EmployeeChosen?.Invoke();
    }

    private bool Can(PermissionAction action) =>
        !IsBusy && _session.Current is not null && _authorization.Current.Allows(Permission, action);

    private void UpdateCommands()
    {
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ChooseCommand.NotifyCanExecuteChanged();
    }
}
internal sealed record EmployeeCatalogRow(long Id, string Code, string Name, string Phone, string Email,
    string StatusCode, bool IsActive)
{
    public string Address => string.Empty;
}
