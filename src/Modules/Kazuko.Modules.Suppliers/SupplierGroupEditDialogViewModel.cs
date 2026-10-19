using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierGroupEditDialogViewModel : ObservableObject
{
    private readonly ISupplierRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly long? _groupId;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _errorMessage = string.Empty;

    public SupplierGroupEditDialogViewModel(
        ISupplierRepository repository,
        ICurrentSessionAccessor sessionAccessor,
        SupplierGroupDetail? existing)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _groupId = existing?.Id;
        if (existing is not null)
        {
            _name = existing.Name;
            _description = existing.Description;
        }

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsSaving);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public event Action<bool>? RequestClose;

    public string DialogTitle => _groupId.HasValue ? "Sửa nhóm nhà cung cấp" : "Thêm nhóm nhà cung cấp";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsSaving
    {
        get;
        private set;
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task SaveAsync()
    {
        if (IsSaving)
            return;

        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Tên nhóm là bắt buộc.";
            return;
        }

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        IsSaving = true;
        ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
        try
        {
            await _repository.SaveGroupAsync(
                new SupplierGroupSaveRequest(_groupId, Name.Trim(), Description.Trim()),
                session);
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
            ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
        }
    }
}
