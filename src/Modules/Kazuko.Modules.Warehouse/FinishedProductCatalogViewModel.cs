using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

internal sealed class FinishedProductCatalogViewModel : ObservableObject
{
    private readonly IWarehouseRepository _repository;
    private readonly AuthenticatedSession _session;
    private readonly IAuthorizationService _authorization;
    private readonly bool _symbols;
    private readonly Func<FinishedProductCatalogItem?, long?> _edit;
    private readonly Func<string, bool> _confirm;
    private IReadOnlyList<FinishedProductCatalogItem> _all = [];
    private IReadOnlyList<FinishedProductCatalogItem> _rows = [];
    private FinishedProductCatalogItem? _selected;
    private string _query = string.Empty;
    private string _error = string.Empty;
    private bool _isBusy;

    public FinishedProductCatalogViewModel(IWarehouseRepository repository, AuthenticatedSession session,
        IAuthorizationService authorization, bool symbols, Func<FinishedProductCatalogItem?, long?> edit,
        Func<string, bool> confirm)
    {
        _repository = repository;
        _session = session;
        _authorization = authorization;
        _symbols = symbols;
        _edit = edit;
        _confirm = confirm;
        AddCommand = new AsyncRelayCommand(() => EditAsync(null), () => Can(PermissionAction.Add));
        EditCommand = new AsyncRelayCommand(() => EditAsync(Selected), () => Selected is not null && Can(PermissionAction.Edit));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => Selected is { IsActive: true } && Can(PermissionAction.Delete));
        ChooseCommand = new RelayCommand(() => { ChosenItem = Selected; Chosen?.Invoke(); },
            () => Selected is { IsActive: true } && Can(PermissionAction.View));
    }

    public bool CanChoose => true;
    public string SearchHint => _symbols ? "Tìm ký hiệu" : "Tìm theo mã hoặc tên nhóm hàng";
    public string DeleteHint => "Ngừng dùng, giữ nguyên lịch sử sản phẩm";
    public string SelectedContext => _symbols ? "Ký hiệu thành phẩm" : "Nhóm hàng thành phẩm";
    public string? SelectedGroupName => null;
    public bool HasChanges { get; private set; }
    public FinishedProductCatalogItem? ChosenItem { get; private set; }
    public event Action? Chosen;
    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand EditCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand ChooseCommand { get; }
    public IReadOnlyList<FinishedProductCatalogItem> Rows { get => _rows; private set => SetProperty(ref _rows, value); }
    public FinishedProductCatalogItem? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) NotifyCommands(); }
    }
    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value)) Filter(Selected?.Id); }
    }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) NotifyCommands(); }
    }

    public async Task LoadAsync(long? selectedId)
    {
        IsBusy = true;
        Error = string.Empty;
        try { await ReloadAsync(selectedId); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync(long? selectedId)
    {
        await _authorization.DemandAsync([new("INV_FINISHED_RECEIPT")]);
        _all = await _repository.GetFinishedProductCatalogAsync(_symbols, _session);
        Filter(selectedId);
    }

    private void Filter(long? selectedId)
    {
        var query = Query.Trim();
        Rows = _all.Where(x => x.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
            || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        Selected = Rows.FirstOrDefault(x => x.Id == selectedId) ?? Rows.FirstOrDefault();
    }

    private async Task EditAsync(FinishedProductCatalogItem? item)
    {
        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _authorization.DemandAsync([new("INV_FINISHED_RECEIPT", item is null ? PermissionAction.Add : PermissionAction.Edit)]);
            if (_edit(item) is not long id) return;
            HasChanges = true;
            Query = string.Empty;
            await ReloadAsync(id);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { IsActive: true } item || !_confirm($"Ngừng dùng {item.Name}? Lịch sử sản phẩm sẽ được giữ lại.")) return;
        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _authorization.DemandAsync([new("INV_FINISHED_RECEIPT", PermissionAction.Delete)]);
            await _repository.DeactivateFinishedProductCatalogAsync(_symbols, item.Id, _session);
            HasChanges = true;
            _all = _all.Select(x => x.Id == item.Id ? x with { IsActive = false } : x).ToArray();
            Filter(item.Id);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private bool Can(PermissionAction action) => !IsBusy && _authorization.Current.Allows("INV_FINISHED_RECEIPT", action);
    private void NotifyCommands()
    {
        AddCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ChooseCommand.NotifyCanExecuteChanged();
    }
}