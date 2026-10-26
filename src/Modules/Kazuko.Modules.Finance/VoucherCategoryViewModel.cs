using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Finance;

public sealed class VoucherCategoryViewModel : ObservableObject
{
    private readonly IVoucherCategoryService _service;
    private readonly AuthenticatedSession _session;
    private readonly IAuthorizationService? _permissions;
    private readonly string _type;
    private VoucherCategory? _selected;
    private long? _editingId;
    private string _code = string.Empty, _name = string.Empty, _error = string.Empty, _status = string.Empty;
    private bool _active = true, _busy;
    public VoucherCategoryViewModel(IVoucherCategoryService service, AuthenticatedSession session, string type, IAuthorizationService? permissions)
    {
        _service = service; _session = session; _type = VoucherCategoryRules.NormalizeType(type); _permissions = permissions;
        Title = _type == FinancialVoucherTypes.In ? "Danh mục loại thu" : "Danh mục loại chi";
        NewCommand = new AsyncRelayCommand(NewAsync, () => !IsBusy && Allows(PermissionAction.Add));
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && Allows(_editingId is null ? PermissionAction.Add : PermissionAction.Edit));
        ReloadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsBusy && SelectedItem is not null && Allows(PermissionAction.Delete));
    }
    public string Title { get; }
    public string CreateTitle => _type == FinancialVoucherTypes.In ? "Thêm loại thu" : "Thêm loại chi";
    public ObservableCollection<VoucherCategory> Items { get; } = [];
    public VoucherCategory? SelectedItem
    {
        get => _selected;
        set { if (!SetProperty(ref _selected, value)) return; if (value is not null) { _editingId = value.Id; Code = value.Code; Name = value.Name; IsActive = value.IsActive; Status = "Đang sửa loại đã chọn."; } NotifyCommands(); }
    }
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public bool IsActive { get => _active; set => SetProperty(ref _active, value); }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) { OnPropertyChanged(nameof(CanEdit)); NotifyCommands(); } } }
    public bool CanEdit => !IsBusy;
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand DeleteCommand { get; }
    public Func<VoucherCategory, bool>? ConfirmDelete { get; set; }
    public Func<Task>? RequestCreate { get; set; }
    public event Action<VoucherCategory>? Saved;
    public VoucherCategoryViewModel CreateNewEditor() => new(_service, _session, _type, _permissions);
    public Task ReloadAndSelectAsync(long id) { _editingId = id; return LoadAsync(); }
    private bool Allows(PermissionAction action) => _permissions?.Current.Allows(_type == FinancialVoucherTypes.In ? "FIN_CASH_IN" : "FIN_CASH_OUT", action) ?? true;
    public async Task LoadAsync()
    {
        IsBusy = true; Error = string.Empty;
        try
        {
            var selectedId = _editingId;
            var rows = await _service.ListAsync(_type, true, _session);
            Items.Clear(); foreach (var row in rows) Items.Add(row);
            SelectedItem = Items.FirstOrDefault(x => x.Id == selectedId);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task NewAsync()
    {
        if (RequestCreate is not null)
        {
            try { await RequestCreate(); }
            catch (Exception ex) { Error = $"Không mở được form thêm loại: {ex.Message}"; }
            return;
        }
        _editingId = null; SelectedItem = null; Code = Name = string.Empty; IsActive = true;
        Error = string.Empty; Status = "Đang thêm loại mới."; NotifyCommands();
    }
    private async Task SaveAsync()
    {
        IsBusy = true; Error = string.Empty;
        try
        {
            var request = VoucherCategoryRules.Normalize(new(_editingId, _type, Code, Name, IsActive));
            var saved = await _service.SaveAsync(request, _session);
            _editingId = saved.Id; await LoadAsync(); Status = "Đã lưu loại thu/chi.";
            Saved?.Invoke(saved);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task DeleteAsync()
    {
        var selected = SelectedItem;
        if (selected is null || !Allows(PermissionAction.Delete) || ConfirmDelete?.Invoke(selected) != true) return;
        IsBusy = true; Error = string.Empty;
        try
        {
            if (_permissions is not null) await _permissions.DemandAsync([new(_type == FinancialVoucherTypes.In ? "FIN_CASH_IN" : "FIN_CASH_OUT", PermissionAction.Delete)]);
            await _service.DeleteAsync(selected.Id, _type, _session);
            _editingId = null; SelectedItem = null; Code = Name = string.Empty; IsActive = true;
            await LoadAsync(); Status = "Đã xóa loại thu/chi.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private void NotifyCommands()
    {
        ((AsyncRelayCommand)NewCommand).NotifyCanExecuteChanged(); ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged(); ((AsyncRelayCommand)ReloadCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DeleteCommand).NotifyCanExecuteChanged();
    }
}
