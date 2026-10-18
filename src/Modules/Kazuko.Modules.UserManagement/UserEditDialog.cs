using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.UserManagement;

public sealed class UserEditDialogViewModel : ObservableObject
{
    private readonly IUserManagementRepository _repository;
    private readonly long? _userId;
    private readonly long _createdBy;
    private readonly long? _branchId;
    private readonly long? _warehouseId;

    private EmployeeOption? _selectedEmployee;
    private UserGroupOption? _selectedGroup;
    private string _email = string.Empty;
    private string _username = string.Empty;
    private string _notes = string.Empty;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;

    public UserEditDialogViewModel(
        IUserManagementRepository repository,
        ICurrentSessionAccessor session,
        IReadOnlyList<EmployeeOption> employees,
        IReadOnlyList<UserGroupOption> groups,
        SystemUser? existing)
    {
        _repository = repository;
        _createdBy = session.Current?.UserId ?? 0;
        _branchId = session.Current?.BranchId;
        _warehouseId = existing?.WarehouseId;

        Employees = employees;
        Groups = groups;

        Title = existing is null ? "Thêm người dùng" : "Sửa người dùng";
        IsCreate = existing is null;
        PasswordHint = existing is null
            ? "Mật khẩu là bắt buộc khi thêm mới."
            : "Để trống nếu giữ nguyên mật khẩu cũ.";

        if (existing is not null)
        {
            _userId = existing.Id;
            _username = existing.Username;
            _email = existing.Email ?? string.Empty;
            _notes = existing.Notes ?? string.Empty;
            _isActive = existing.IsActive;

            if (existing.EmployeeId is not null)
                SelectedEmployee = employees.FirstOrDefault(e => e.Id == existing.EmployeeId);
            if (existing.GroupId is not null)
                SelectedGroup = groups.FirstOrDefault(g => g.Id == existing.GroupId);
        }

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }

    public string Title { get; }
    public bool IsCreate { get; }
    public string PasswordHint { get; }

    public IReadOnlyList<EmployeeOption> Employees { get; }
    public IReadOnlyList<UserGroupOption> Groups { get; }

    public EmployeeOption? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetProperty(ref _selectedEmployee, value) && value is not null)
            {
                // Tự điền email từ hồ sơ nhân viên nếu đang trống.
                if (string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(value.Email))
                    Email = value.Email;
            }
        }
    }

    public UserGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    /// <summary>Mật khẩu nhập từ PasswordBox (code-behind). Rỗng khi sửa = giữ nguyên.</summary>
    public string Password { get; set; } = string.Empty;

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
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

    public SystemUser? SavedUser { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;

        if (SelectedEmployee is null)
        {
            ErrorMessage = "Vui lòng chọn nhân viên.";
            return;
        }
        if (SelectedGroup is null)
        {
            ErrorMessage = "Vui lòng chọn nhóm người dùng.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Tài khoản là bắt buộc.";
            return;
        }
        if (IsCreate && string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Mật khẩu là bắt buộc khi thêm mới.";
            return;
        }

        try
        {
            SavedUser = await _repository.SaveUserAsync(
                new UserSaveRequest(
                    _userId,
                    SelectedEmployee.Id,
                    _warehouseId,
                    Email.Trim(),
                    SelectedGroup.Id,
                    Username.Trim(),
                    string.IsNullOrWhiteSpace(Password) ? null : Password,
                    Notes.Trim(),
                    IsActive,
                    _createdBy,
                    _branchId));
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

public partial class UserEditDialog : Window
{
    public UserEditDialog()
    {
        InitializeComponent();
    }

    public static SystemUser? ShowForCreate(
        Window? owner,
        IUserManagementRepository repository,
        ICurrentSessionAccessor session,
        UserGroup selectedGroup)
    {
        var viewModel = CreateViewModel(repository, session, null, selectedGroup.Id);
        return ShowInternal(owner, viewModel);
    }

    public static SystemUser? ShowForEdit(
        Window? owner,
        IUserManagementRepository repository,
        ICurrentSessionAccessor session,
        SystemUser existing)
    {
        var viewModel = CreateViewModel(repository, session, existing, null);
        return ShowInternal(owner, viewModel);
    }

    private static UserEditDialogViewModel CreateViewModel(
        IUserManagementRepository repository,
        ICurrentSessionAccessor session,
        SystemUser? existing,
        long? preselectedGroupId)
    {
        var employees = repository.GetEmployeesAsync().GetAwaiter().GetResult();
        var groups = repository.GetGroupsAsync().GetAwaiter().GetResult()
            .Where(g => g.IsActive)
            .Select(g => new UserGroupOption(g.Id, g.Code, g.Name))
            .ToArray();

        var viewModel = new UserEditDialogViewModel(
            repository, session, employees, groups, existing);

        if (existing is null && preselectedGroupId is not null)
            viewModel.SelectedGroup = groups.FirstOrDefault(g => g.Id == preselectedGroupId);

        return viewModel;
    }

    private static SystemUser? ShowInternal(Window? owner, UserEditDialogViewModel viewModel)
    {
        var dialog = new UserEditDialog { DataContext = viewModel };
        if (owner is not null)
            dialog.Owner = owner;

        var saved = false;
        viewModel.RequestClose = result =>
        {
            saved = result;
            dialog.Close();
        };

        dialog.ShowDialog();
        return saved ? viewModel.SavedUser : null;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UserEditDialogViewModel viewModel
            && sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            viewModel.Password = passwordBox.Password;
        }
    }
}
