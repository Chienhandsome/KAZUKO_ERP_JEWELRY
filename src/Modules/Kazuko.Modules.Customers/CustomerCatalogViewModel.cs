using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Customers;

internal sealed class CustomerCatalogViewModel : ObservableObject
{
    private const string Permission = "CAT_CUSTOMERS";
    private readonly ICustomerRepository _repository;
    private readonly ICurrentSessionAccessor _session;
    private readonly IAuthorizationService _authorization;
    private readonly ICustomerEditorService _editor;
    private readonly bool _allowWalkIn;
    private readonly Func<string, string, bool> _confirm;
    private IReadOnlyList<CustomerListItem> _allCustomers = [];
    private IReadOnlyList<CustomerListItem> _customers = [];
    private CustomerListItem? _selected;
    private string _query = string.Empty;
    private string _error = string.Empty;
    private bool _isBusy;

    public CustomerCatalogViewModel(ICustomerRepository repository, ICurrentSessionAccessor session,
        IAuthorizationService authorization, ICustomerEditorService editor, bool selectCustomer,
        bool allowWalkIn, Func<string, string, bool> confirm)
    {
        _repository = repository;
        _session = session;
        _authorization = authorization;
        _editor = editor;
        _allowWalkIn = allowWalkIn;
        _confirm = confirm;
        CanChoose = selectCustomer;
        AddCommand = new AsyncRelayCommand(() => OpenEditorAsync(null), () => Can(PermissionAction.Add));
        EditCommand = new AsyncRelayCommand(() => OpenEditorAsync(Selected?.Id),
            () => Selected is { IsWalkIn: false } && Can(PermissionAction.Edit));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync,
            () => Selected is { IsActive: true, IsWalkIn: false } && Can(PermissionAction.Delete));
        ChooseCommand = new RelayCommand(Choose,
            () => CanChoose && Selected is { IsActive: true } && Can(PermissionAction.View));
    }

    public bool CanChoose { get; }
    public string SearchHint => "Tìm theo tên, mã hoặc số điện thoại";
    public string DeleteHint => "Ngừng giao dịch với khách hàng";
    public string? SelectedGroupName => Selected?.CustomerGroupName;
    public string SelectedContext => Selected?.IsWalkIn == true ? "Hồ sơ khách vãng lai" : string.Empty;
    public bool HasChanges { get; private set; }
    public CustomerEditResult? ChosenCustomer { get; private set; }
    public event Action? CustomerChosen;

    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand ChooseCommand { get; }

    public IReadOnlyList<CustomerListItem> Rows
    {
        get => _customers;
        private set => SetProperty(ref _customers, value);
    }

    public CustomerListItem? Selected
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
            _allCustomers = [];
            ApplyFilter(null);
            Error = "Không thể tải danh mục khách hàng. Vui lòng đóng và mở lại danh mục.";
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync(long? selectedId)
    {
        await _authorization.DemandAsync([new(Permission)]);
        if (_session.Current is null)
            throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

        _allCustomers = await _repository.SearchAsync(new CustomerSearchFilter(string.Empty, 0,
            string.Empty, null, _allowWalkIn ? null : false));
        ApplyFilter(selectedId);
    }

    private void ApplyFilter(long? selectedId)
    {
        var query = Query.Trim();
        var customers = query.Length == 0 ? _allCustomers : _allCustomers.Where(row =>
            row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Code.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Phone.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Email.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.TaxCode.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        Rows = customers;
        Selected = customers.FirstOrDefault(row => row.Id == selectedId) ?? customers.FirstOrDefault();
    }

    private async Task OpenEditorAsync(long? id)
    {
        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _authorization.DemandAsync([new(Permission, id.HasValue ? PermissionAction.Edit : PermissionAction.Add)]);

            var result = id is long existingId ? _editor.ShowEdit(existingId) : _editor.ShowCreate();
            if (result is null)
                return;

            HasChanges = true;
            Query = string.Empty;
            Selected = null;
            await ReloadAsync(result.Id);
        }
        catch (Exception)
        {
            Error = "Không thể cập nhật danh mục khách hàng. Vui lòng đóng và mở lại danh mục.";
        }
        finally { IsBusy = false; }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { IsActive: true, IsWalkIn: false } row || _session.Current is not { } session)
            return;
        if (!_confirm($"Ngừng giao dịch với {row.Code} – {row.Name}? Lịch sử giao dịch sẽ được giữ lại.",
                "Xóa khách hàng"))
            return;

        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _authorization.DemandAsync([new(Permission, PermissionAction.Delete)]);
            await _repository.SetActiveAsync(row.Id, false, session);
            HasChanges = true;
            _allCustomers = _allCustomers.Select(customer => customer.Id == row.Id
                ? customer with { IsActive = false }
                : customer).ToArray();
            ApplyFilter(row.Id);
        }
        catch (Exception)
        {
            Error = "Không thể ngừng giao dịch với khách hàng này.";
        }
        finally { IsBusy = false; }
    }

    private void Choose()
    {
        if (Selected is not { IsActive: true } row)
            return;
        ChosenCustomer = new CustomerEditResult(row.Id, row.Code, row.Name, row.Phone, row.Address, row.IsActive, row.IsWalkIn);
        CustomerChosen?.Invoke();
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
