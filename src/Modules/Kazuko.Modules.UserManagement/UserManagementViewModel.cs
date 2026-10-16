using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.UserManagement;

/// <summary>
/// Màn hình Người dùng & Phân quyền:
/// - Pane trái: danh sách NHÓM người dùng (không có "Tất cả").
/// - Pane phải: tab "Người dùng" (bảng Email | Họ tên | Tài khoản, ẩn tài khoản
///   Admin hệ thống) và tab "Quyền sử dụng" (ma trận quyền theo nhóm đang chọn).
/// </summary>
public sealed class UserManagementViewModel : ObservableObject
{
    private readonly IUserManagementRepository _repository;
    private readonly ICurrentSessionAccessor _session;

    private readonly ObservableCollection<SystemUser> _allUsers = new();

    private UserGroup? _selectedGroup;
    private SystemUser? _selectedUser;
    private string _keyword = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public UserManagementViewModel(
        IUserManagementRepository repository,
        ICurrentSessionAccessor session)
    {
        _repository = repository;
        _session = session;

        Groups = new ObservableCollection<UserGroup>();
        Users = new ObservableCollection<SystemUser>();
        PermissionRows = new ObservableCollection<PermissionMatrixRowViewModel>();
        GroupedPermissions = CollectionViewSource.GetDefaultView(PermissionRows);
        GroupedPermissions.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PermissionMatrixRowViewModel.ModuleGroup)));

        InheritanceExplanation = PermissionInheritanceRules.Explanation;

        AddGroupCommand = new AsyncRelayCommand(AddGroupAsync, () => !IsBusy);
        EditGroupCommand = new AsyncRelayCommand(EditGroupAsync, () => !IsBusy && SelectedGroup is not null);
        DeleteGroupCommand = new AsyncRelayCommand(DeleteGroupAsync, () => !IsBusy && SelectedGroup is not null);
        AddUserCommand = new AsyncRelayCommand(AddUserAsync, () => !IsBusy && SelectedGroup is not null);
        EditUserCommand = new AsyncRelayCommand(EditUserAsync, () => !IsBusy && SelectedUser is not null);
        DeleteUserCommand = new AsyncRelayCommand(DeleteUserAsync, () => !IsBusy && SelectedUser is not null);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => !IsBusy && Users.Count > 0);
        SavePermissionsCommand = new AsyncRelayCommand(SavePermissionsAsync, () => !IsBusy && SelectedGroup is not null);

        _ = RefreshAsync();
    }

    public ObservableCollection<UserGroup> Groups { get; }
    public ObservableCollection<SystemUser> Users { get; }
    public ObservableCollection<PermissionMatrixRowViewModel> PermissionRows { get; }
    public ICollectionView GroupedPermissions { get; }

    public string InheritanceExplanation { get; }

    public string Title => "Người dùng & Phân quyền";

    public string SelectedGroupTitle =>
        SelectedGroup is null ? "Người dùng" : $"Người dùng — {SelectedGroup.Name}";

    public string PermissionGroupTitle =>
        SelectedGroup is null ? "Quyền sử dụng" : $"Quyền sử dụng — {SelectedGroup.Name}";

    public UserGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                OnPropertyChanged(nameof(SelectedGroupTitle));
                OnPropertyChanged(nameof(PermissionGroupTitle));
                NotifyCommands();
                _ = LoadGroupDetailAsync();
            }
        }
    }

    public SystemUser? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value))
                NotifyCommands();
        }
    }

    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                NotifyCommands();
        }
    }

    public ICommand AddGroupCommand { get; }
    public ICommand EditGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand AddUserCommand { get; }
    public ICommand EditUserCommand { get; }
    public ICommand DeleteUserCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand SavePermissionsCommand { get; }

    private void NotifyCommands()
    {
        ((AsyncRelayCommand)AddGroupCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)EditGroupCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DeleteGroupCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddUserCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)EditUserCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DeleteUserCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SavePermissionsCommand).NotifyCanExecuteChanged();
    }

    private async Task RefreshAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var groups = await _repository.GetGroupsAsync();
            var selectedId = SelectedGroup?.Id;
            Groups.Clear();
            foreach (var group in groups)
                Groups.Add(group);

            SelectedGroup = Groups.FirstOrDefault(g => g.Id == selectedId)
                ?? Groups.FirstOrDefault();
            if (SelectedGroup is null)
                await LoadGroupDetailAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadGroupDetailAsync()
    {
        ErrorMessage = string.Empty;
        Users.Clear();
        _allUsers.Clear();
        PermissionRows.Clear();

        if (SelectedGroup is null)
            return;

        IsBusy = true;
        try
        {
            var users = await _repository.GetUsersAsync(SelectedGroup.Id);
            foreach (var user in users)
                _allUsers.Add(user);
            ApplyUserFilter();

            var saved = await _repository.GetGroupPermissionsAsync(SelectedGroup.Id);
            foreach (var row in PermissionMatrixRowViewModel.BuildFrom(saved))
                PermissionRows.Add(row);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task SearchAsync()
    {
        ApplyUserFilter();
        return Task.CompletedTask;
    }

    private void ApplyUserFilter()
    {
        var keyword = Keyword.Trim();
        Users.Clear();
        foreach (var user in _allUsers)
        {
            if (keyword.Length == 0 ||
                user.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                user.Username.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (user.Email ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                Users.Add(user);
            }
        }
        SelectedUser = Users.FirstOrDefault();
        ((AsyncRelayCommand)PrintCommand).NotifyCanExecuteChanged();
    }

    private async Task AddGroupAsync()
    {
        var saved = UserGroupEditDialog.ShowForCreate(ActiveWindow(), _repository, _session);
        if (saved is not null)
            await RefreshAsync();
    }

    private async Task EditGroupAsync()
    {
        if (SelectedGroup is null)
            return;
        var saved = UserGroupEditDialog.ShowForEdit(ActiveWindow(), _repository, _session, SelectedGroup);
        if (saved is not null)
            await RefreshAsync();
    }

    private async Task DeleteGroupAsync()
    {
        if (SelectedGroup is null)
            return;

        var answer = MessageBox.Show(
            $"Xóa nhóm \"{SelectedGroup.Name}\"?\nMa trận quyền của nhóm cũng bị xóa.",
            "Xóa nhóm người dùng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            await _repository.DeleteGroupAsync(SelectedGroup.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Xóa nhóm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task AddUserAsync()
    {
        if (SelectedGroup is null)
            return;
        var saved = UserEditDialog.ShowForCreate(ActiveWindow(), _repository, _session, SelectedGroup);
        if (saved is not null)
            await LoadGroupDetailAsync();
    }

    private async Task EditUserAsync()
    {
        if (SelectedUser is null)
            return;
        var saved = UserEditDialog.ShowForEdit(ActiveWindow(), _repository, _session, SelectedUser);
        if (saved is not null)
            await LoadGroupDetailAsync();
    }

    private async Task DeleteUserAsync()
    {
        if (SelectedUser is null)
            return;

        var answer = MessageBox.Show(
            $"Xóa tài khoản \"{SelectedUser.Username}\" ({SelectedUser.DisplayName})?",
            "Xóa người dùng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            await _repository.DeleteUserAsync(SelectedUser.Id);
            await LoadGroupDetailAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Xóa người dùng", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Task PrintAsync()
    {
        if (SelectedGroup is null || Users.Count == 0)
            return Task.CompletedTask;

        var document = new FlowDocument
        {
            PagePadding = new Thickness(40),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        };
        document.Blocks.Add(new Paragraph(new Run($"Danh sách người dùng — {SelectedGroup.Name}"))
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
        });
        document.Blocks.Add(new Paragraph(new Run($"Ngày in: {DateTime.Now:dd/MM/yyyy HH:mm}"))
        {
            TextAlignment = TextAlignment.Right,
            FontSize = 11,
        });

        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(60) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(140) });

        var headerGroup = new TableRowGroup();
        var headerRow = new TableRow();
        foreach (var text in new[] { "STT", "Họ tên", "Tài khoản", "Email" })
            headerRow.Cells.Add(new TableCell(new Paragraph(new Run(text)) { FontWeight = FontWeights.Bold }));
        headerGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerGroup);

        var bodyGroup = new TableRowGroup();
        var index = 1;
        foreach (var user in Users)
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run(index.ToString()))));
            row.Cells.Add(new TableCell(new Paragraph(new Run(user.DisplayName))));
            row.Cells.Add(new TableCell(new Paragraph(new Run(user.Username))));
            row.Cells.Add(new TableCell(new Paragraph(new Run(user.Email ?? string.Empty))));
            bodyGroup.Rows.Add(row);
            index++;
        }
        table.RowGroups.Add(bodyGroup);
        document.Blocks.Add(table);

        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
            printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, Title);

        return Task.CompletedTask;
    }

    private async Task SavePermissionsAsync()
    {
        if (SelectedGroup is null)
            return;

        var session = _session.Current;
        if (session is null)
        {
            MessageBox.Show("Phiên đăng nhập đã hết hạn.", "Lưu quyền", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var rows = PermissionRows.Select(r => r.ToSaveRow()).ToArray();
            await _repository.SaveGroupPermissionsAsync(SelectedGroup.Id, rows, session.UserId);
            MessageBox.Show(
                $"Đã lưu ma trận quyền cho nhóm \"{SelectedGroup.Name}\".",
                "Lưu quyền",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lưu quyền", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
}
