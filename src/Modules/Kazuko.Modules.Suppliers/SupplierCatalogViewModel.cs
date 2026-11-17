using Kazuko.Core.Authentication;
using Kazuko.Core.Suppliers;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Suppliers;

internal sealed class SupplierCatalogViewModel : ObservableObject
{
    private const string Permission = "CAT_SUPPLIERS";
    private readonly ISupplierRepository _repository;
    private readonly ICurrentSessionAccessor _session;
    private readonly IAuthorizationService _authorization;
    private readonly Func<long?, SupplierEditResult?> _openEditor;
    private readonly Func<string, bool> _confirm;
    private IReadOnlyList<SupplierListItem> _allSuppliers = [];
    private IReadOnlyList<SupplierListItem> _customers = [];
    private SupplierListItem? _selected;
    private string _query = string.Empty;
    private string _error = string.Empty;
    private bool _isBusy;

    public SupplierCatalogViewModel(ISupplierRepository repository, ICurrentSessionAccessor session,
        IAuthorizationService authorization, Func<long?, SupplierEditResult?> openEditor, bool selectSupplier,
        Func<string, bool> confirm)
    {
        _repository = repository;
        _session = session;
        _authorization = authorization;
        _openEditor = openEditor;
        _confirm = confirm;
        CanChoose = selectSupplier;
        AddCommand = new AsyncRelayCommand(() => OpenEditorAsync(null), () => Can(PermissionAction.Add));
        EditCommand = new AsyncRelayCommand(() => OpenEditorAsync(Selected?.Id),
            () => Selected is not null && Can(PermissionAction.Edit));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync,
            () => Selected is { IsActive: true } && Can(PermissionAction.Delete));
        ChooseCommand = new RelayCommand(Choose,
            () => CanChoose && Selected is { IsActive: true } && Can(PermissionAction.View));
    }

    public bool CanChoose { get; }
    public string SearchHint => "Tìm theo tên, mã, số điện thoại hoặc mã số thuế";
    public string DeleteHint => "Ngừng giao dịch với nhà cung cấp";
    public string? SelectedGroupName => Selected?.SupplierGroupName;
    public string SelectedContext => Selected is { TaxCode.Length: > 0 } row ? $"MST: {row.TaxCode}" : string.Empty;
    public bool HasChanges { get; private set; }
    public SupplierEditResult? ChosenSupplier { get; private set; }
    public event Action? SupplierChosen;

    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand ChooseCommand { get; }

    public IReadOnlyList<SupplierListItem> Rows
    {
        get => _customers;
        private set => SetProperty(ref _customers, value);
    }

    public SupplierListItem? Selected
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
            _allSuppliers = [];
            ApplyFilter(null);
            Error = "Không thể tải danh mục nhà cung cấp. Vui lòng đóng và mở lại danh mục.";
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync(long? selectedId)
    {
        await _authorization.DemandAsync([new(Permission)]);
        if (_session.Current is null)
            throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

        _allSuppliers = await _repository.SearchAsync(new SupplierSearchFilter(string.Empty, 0,
            string.Empty, null));
        ApplyFilter(selectedId);
    }

    private void ApplyFilter(long? selectedId)
    {
        var query = Query.Trim();
        var suppliers = query.Length == 0 ? _allSuppliers : _allSuppliers.Where(row =>
            row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Code.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Phone.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.Email.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.TaxCode.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        Rows = suppliers;
        Selected = suppliers.FirstOrDefault(row => row.Id == selectedId) ?? suppliers.FirstOrDefault();
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
            Error = "Không thể cập nhật danh mục nhà cung cấp. Vui lòng đóng và mở lại danh mục.";
        }
        finally { IsBusy = false; }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { IsActive: true } row || _session.Current is not { } session)
            return;
        if (!_confirm($"Ngừng giao dịch với {row.Code} – {row.Name}? Lịch sử giao dịch sẽ được giữ lại."))
            return;

        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _authorization.DemandAsync([new(Permission, PermissionAction.Delete)]);
            await _repository.SetActiveAsync(row.Id, false, session);
            HasChanges = true;
            _allSuppliers = _allSuppliers.Select(supplier => supplier.Id == row.Id
                ? supplier with { IsActive = false }
                : supplier).ToArray();
            ApplyFilter(row.Id);
        }
        catch (Exception)
        {
            Error = "Không thể ngừng giao dịch với nhà cung cấp này.";
        }
        finally { IsBusy = false; }
    }

    private void Choose()
    {
        if (Selected is not { IsActive: true } row)
            return;
        ChosenSupplier = new SupplierEditResult(row.Id, row.Code, row.Name, row.Phone, row.Address, row.IsActive);
        SupplierChosen?.Invoke();
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
