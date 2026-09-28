using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.HumanResources;

public sealed class HumanResourcesViewModel : ObservableObject
{
    private readonly IHumanResourcesRepository _repository;
    private readonly IEmployeeEditorService _editor;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private CancellationTokenSource? _searchCts;
    private string _searchText = string.Empty;
    private string _selectedStatus = "Tất cả";
    private EmployeeRowViewModel? _selectedEmployee;
    private EmployeeDetail? _selectedDetail;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isEmpty;
    private int _selectedTabIndex;

    public HumanResourcesViewModel(
        IHumanResourcesRepository repository,
        IEmployeeEditorService editor,
        ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _editor = editor;
        _sessionAccessor = sessionAccessor;

        StatusOptions.Add("Tất cả");
        StatusOptions.Add("Đang làm việc");
        StatusOptions.Add("Nghỉ phép");
        StatusOptions.Add("Ngừng làm việc");

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        AddCommand = new RelayCommand(AddEmployee, () => !IsLoading);
        EditCommand = new ParameterRelayCommand(
            EditEmployee,
            parameter => !IsLoading && (parameter is EmployeeRowViewModel || SelectedEmployee is not null));
        EditSelectedCommand = new RelayCommand(EditSelected, () => !IsLoading && SelectedEmployee is not null);
        ToggleActiveCommand = new ParameterRelayCommand(
            ToggleActive,
            parameter => !IsLoading && (parameter is EmployeeRowViewModel || SelectedEmployee is not null));
        ExportExcelCommand = new RelayCommand(
            () => MessageBox.Show(
                "Xuất Excel sẽ được bổ sung ở giai đoạn sau.",
                "Nhân sự",
                MessageBoxButton.OK,
                MessageBoxImage.Information),
            () => true);

        _ = InitializeAsync();
    }

    public ObservableCollection<EmployeeRowViewModel> Employees { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value);
    }

    public EmployeeRowViewModel? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetProperty(ref _selectedEmployee, value))
            {
                ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
                _ = LoadSelectedDetailAsync();
            }
        }
    }

    public EmployeeDetail? SelectedDetail
    {
        get => _selectedDetail;
        private set
        {
            if (SetProperty(ref _selectedDetail, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedCode));
                OnPropertyChanged(nameof(SelectedFullName));
                OnPropertyChanged(nameof(SelectedGender));
                OnPropertyChanged(nameof(SelectedDateOfBirthText));
                OnPropertyChanged(nameof(SelectedAttendanceCode));
                OnPropertyChanged(nameof(SelectedPhone));
                OnPropertyChanged(nameof(SelectedEmail));
                OnPropertyChanged(nameof(SelectedAddress));
                OnPropertyChanged(nameof(SelectedHireDateText));
                OnPropertyChanged(nameof(SelectedContractType));
                OnPropertyChanged(nameof(SelectedBaseSalaryText));
                OnPropertyChanged(nameof(SelectedAllowanceText));
                OnPropertyChanged(nameof(SelectedNotes));
                OnPropertyChanged(nameof(SelectedStatusText));
                OnPropertyChanged(nameof(SelectedPhotoPath));
                OnPropertyChanged(nameof(HasPhoto));
            }
        }
    }

    public bool HasSelection => SelectedDetail is not null;
    public string SelectedCode => SelectedDetail?.Code ?? string.Empty;
    public string SelectedFullName => SelectedDetail?.FullName ?? string.Empty;
    public string SelectedGender => SelectedDetail?.Gender ?? string.Empty;
    public string SelectedDateOfBirthText =>
        SelectedDetail?.DateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public string SelectedAttendanceCode => SelectedDetail?.AttendanceCode ?? string.Empty;
    public string SelectedPhone => SelectedDetail?.Phone ?? string.Empty;
    public string SelectedEmail => SelectedDetail?.Email ?? string.Empty;
    public string SelectedAddress => SelectedDetail?.Address ?? string.Empty;
    public string SelectedHireDateText =>
        SelectedDetail?.HireDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public string SelectedContractType => SelectedDetail?.ContractType ?? string.Empty;
    public string SelectedBaseSalaryText =>
        SelectedDetail?.BaseSalary.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) ?? "0";
    public string SelectedAllowanceText =>
        SelectedDetail?.Allowance.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) ?? "0";
    public string SelectedNotes => SelectedDetail?.Notes ?? string.Empty;
    public string SelectedStatusText =>
        SelectedDetail is null ? string.Empty : EmployeeStatusCodes.ToDisplay(SelectedDetail.StatusCode);
    public string? SelectedPhotoPath => SelectedDetail?.PhotoPath;
    public bool HasPhoto => !string.IsNullOrWhiteSpace(SelectedPhotoPath);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                ((RelayCommand)AddCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditSelectedCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)EditCommand).NotifyCanExecuteChanged();
                ((ParameterRelayCommand)ToggleActiveCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand EditSelectedCommand { get; }
    public ICommand ToggleActiveCommand { get; }
    public ICommand ExportExcelCommand { get; }

    private async Task InitializeAsync() => await SearchAsync();

    private async Task RefreshAsync() => await SearchAsync();

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Đang tải danh sách nhân viên...";

        try
        {
            var statusCode = SelectedStatus switch
            {
                "Đang làm việc" => EmployeeStatusCodes.Working,
                "Nghỉ phép" => EmployeeStatusCodes.OnLeave,
                "Ngừng làm việc" => EmployeeStatusCodes.Terminated,
                _ => string.Empty
            };

            var filter = new EmployeeSearchFilter(SearchText ?? string.Empty, statusCode);
            var items = await _repository.SearchAsync(filter, token);
            if (token.IsCancellationRequested)
                return;

            var selectedId = SelectedEmployee?.Id;
            Employees.Clear();
            var index = 1;
            foreach (var item in items)
                Employees.Add(new EmployeeRowViewModel(item, index++));

            IsEmpty = Employees.Count == 0;
            StatusMessage = IsEmpty
                ? "Không có nhân viên phù hợp bộ lọc."
                : $"Hiển thị {Employees.Count} nhân viên.";

            if (selectedId is long id)
                SelectedEmployee = Employees.FirstOrDefault(e => e.Id == id);
            else
                SelectedEmployee = Employees.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch
        {
            ErrorMessage = "Không thể tải danh sách nhân viên. Kiểm tra kết nối dữ liệu.";
            StatusMessage = string.Empty;
            IsEmpty = Employees.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSelectedDetailAsync()
    {
        if (SelectedEmployee is null)
        {
            SelectedDetail = null;
            return;
        }

        try
        {
            SelectedDetail = await _repository.GetByIdAsync(SelectedEmployee.Id);
        }
        catch
        {
            SelectedDetail = null;
            ErrorMessage = "Không thể tải hồ sơ nhân viên.";
        }
    }

    private void AddEmployee()
    {
        var result = _editor.ShowCreate();
        if (result is not null)
            _ = SearchAsync();
    }

    private void EditSelected() => EditEmployee(SelectedEmployee);

    private void EditEmployee(object? parameter)
    {
        var row = parameter as EmployeeRowViewModel ?? SelectedEmployee;
        if (row is null)
            return;

        var result = _editor.ShowEdit(row.Id);
        if (result is not null)
            _ = SearchAsync();
    }

    private async void ToggleActive(object? parameter)
    {
        var row = parameter as EmployeeRowViewModel ?? SelectedEmployee;
        if (row is null)
            return;

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            MessageBox.Show("Phiên đăng nhập đã hết hạn.", "Nhân sự", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var activate = !row.IsActive;
        var confirm = MessageBox.Show(
            activate
                ? $"Kích hoạt lại nhân viên \"{row.FullName}\"?"
                : $"Ngừng làm việc với nhân viên \"{row.FullName}\"? Lịch sử được giữ lại.",
            "Xác nhận",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            await _repository.SetActiveAsync(row.Id, activate, session);
            await SearchAsync();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Nhân sự", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            MessageBox.Show("Không thể cập nhật trạng thái nhân viên.", "Nhân sự", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
