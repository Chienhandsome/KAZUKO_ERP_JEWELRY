using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.UserManagement;

public sealed class UserGroupEditDialogViewModel : ObservableObject
{
    private readonly IUserManagementRepository _repository;
    private readonly long? _groupId;
    private readonly long _createdBy;

    private string _code = string.Empty;
    private string _name = string.Empty;
    private readonly string? _icon;
    private string _description = string.Empty;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;

    public UserGroupEditDialogViewModel(
        IUserManagementRepository repository,
        ICurrentSessionAccessor session,
        UserGroup? existing)
    {
        _repository = repository;
        _createdBy = session.Current?.UserId ?? 0;

        Title = existing is null ? "Thêm nhóm người dùng" : "Sửa nhóm người dùng";
        if (existing is not null)
        {
            _groupId = existing.Id;
            _code = existing.Code;
            _name = existing.Name;
            _icon = existing.Icon;
            _description = existing.Description ?? string.Empty;
            _isActive = existing.IsActive;
            IsAdminGroup = string.Equals(existing.Code, "ADMIN", StringComparison.OrdinalIgnoreCase);
        }

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public string Title { get; }

    /// <summary>Nhóm ADMIN không cho đổi mã và không cho tắt hoạt động.</summary>
    public bool IsAdminGroup { get; }

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

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

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public UserGroup? SavedGroup { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Tên nhóm là bắt buộc.";
            return;
        }

        try
        {
            SavedGroup = await _repository.SaveGroupAsync(
                new UserGroupSaveRequest(
                    _groupId,
                    IsAdminGroup ? _code : Code,
                    Name.Trim(),
                    _icon,
                    Description.Trim(),
                    IsAdminGroup || IsActive,
                    _createdBy));
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

public partial class UserGroupEditDialog : Window
{
    public UserGroupEditDialog()
    {
        InitializeComponent();
    }

    public static UserGroup? ShowForCreate(
        Window? owner,
        IUserManagementRepository repository,
        ICurrentSessionAccessor session)
    {
        var viewModel = new UserGroupEditDialogViewModel(repository, session, null);
        return ShowInternal(owner, viewModel);
    }

    public static UserGroup? ShowForEdit(
        Window? owner,
        IUserManagementRepository repository,
        ICurrentSessionAccessor session,
        UserGroup existing)
    {
        var viewModel = new UserGroupEditDialogViewModel(repository, session, existing);
        return ShowInternal(owner, viewModel);
    }

    private static UserGroup? ShowInternal(Window? owner, UserGroupEditDialogViewModel viewModel)
    {
        var dialog = new UserGroupEditDialog { DataContext = viewModel };
        if (owner is not null)
            dialog.Owner = owner;

        var saved = false;
        viewModel.RequestClose = result =>
        {
            saved = result;
            dialog.Close();
        };

        dialog.ShowDialog();
        return saved ? viewModel.SavedGroup : null;
    }
}
