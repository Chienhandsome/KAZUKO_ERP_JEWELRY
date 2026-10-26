using System.Collections.ObjectModel;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.HumanResources;

public sealed class WorkShiftCatalogViewModel : ObservableObject
{
    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _session;
    private readonly IAuthorizationService? _permissions;
    private WorkShiftItem? _selected;
    private ShiftEditDialogViewModel? _editor;
    private WorkShiftItem? _saved;
    private bool _loading;
    private string _error = string.Empty, _status = string.Empty;

    public WorkShiftCatalogViewModel(IHumanResourcesRepository repository, ICurrentSessionAccessor session, IAuthorizationService? permissions = null)
    {
        _repository = repository;
        _session = session;
        _permissions = permissions;
        NewCommand = new AsyncRelayCommand(CreateAsync, () => !IsBusy && Allows(PermissionAction.Add));
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && SelectedItem is not null && Allows(PermissionAction.Edit));
        ReloadCommand = new AsyncRelayCommand(() => LoadAsync(SelectedItem?.Id), () => !IsBusy);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsBusy && SelectedItem is not null && Allows(PermissionAction.Delete));
    }

    public ObservableCollection<WorkShiftItem> Items { get; } = [];
    public WorkShiftItem? SelectedItem
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            if (_editor is not null) _editor.RequestClose -= CaptureSaved;
            _editor = value is null ? null : new(_repository, _session, value, _permissions);
            if (_editor is not null) _editor.RequestClose += CaptureSaved;
            OnPropertyChanged(nameof(Editor));
            OnPropertyChanged(nameof(CanEditSelected));
            Status = value is null ? "Chọn ca để xem và chỉnh sửa." : "Đang sửa ca đã chọn.";
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }
    public ShiftEditDialogViewModel? Editor => _editor;
    public bool IsBusy { get => _loading; private set { if (SetProperty(ref _loading, value)) { OnPropertyChanged(nameof(CanSelect)); OnPropertyChanged(nameof(CanEditSelected)); NotifyCommands(); } } }
    public bool CanSelect => !IsBusy;
    public bool CanEditSelected => !IsBusy && Editor is not null && Allows(PermissionAction.Edit);
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public AsyncRelayCommand NewCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public Func<WorkShiftItem, bool>? ConfirmDelete { get; set; }
    public Func<Task<WorkShiftItem?>>? RequestCreate { get; set; }

    private bool Allows(PermissionAction action) => _permissions?.Current.Allows("CAT_EMPLOYEES", action) ?? true;
    private void CaptureSaved(WorkShiftItem? item) => _saved = item;

    public async Task LoadAsync(long? selectId = null)
    {
        IsBusy = true; Error = string.Empty;
        try
        {
            if (_session.Current is null) throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");
            var rows = await _repository.GetWorkShiftsAsync(false);
            SelectedItem = null;
            Items.Clear();
            foreach (var row in rows) Items.Add(row);
            SelectedItem = Items.FirstOrDefault(x => x.Id == selectId) ?? Items.FirstOrDefault();
            if (Items.Count == 0) Status = "Chưa có ca làm việc. Bấm Thêm ca mới để tạo ca.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task CreateAsync()
    {
        Error = string.Empty;
        try
        {
            if (RequestCreate is null) throw new InvalidOperationException("Không mở được form thêm ca. Hãy đóng và mở lại danh mục.");
            var created = await RequestCreate();
            if (created is not null) { await LoadAsync(created.Id); Status = "Đã thêm ca làm việc."; }
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    private async Task SaveAsync()
    {
        var editor = Editor;
        if (editor is null || !Allows(PermissionAction.Edit)) return;
        IsBusy = true; Error = string.Empty; _saved = null;
        try
        {
            await editor.SaveAsync();
            if (_saved is not null) { await LoadAsync(_saved.Id); Status = "Đã lưu ca làm việc."; }
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
            var session = _session.Current ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");
            if (_permissions is not null) await _permissions.DemandAsync([new("CAT_EMPLOYEES", PermissionAction.Delete)]);
            await _repository.DeleteWorkShiftAsync(selected.Id, session);
            await LoadAsync(); Status = "Đã xóa ca làm việc.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    private void NotifyCommands()
    {
        NewCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged(); ReloadCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged();
    }
}
