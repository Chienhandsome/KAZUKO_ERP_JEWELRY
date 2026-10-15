using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Mvvm;
using Microsoft.Win32;

namespace Kazuko.Modules.Customers;

public sealed class CustomerEditDialogViewModel : ObservableObject
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ICustomerRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly long? _customerId;
    private bool _isBusy;
    private bool _isLoading;
    private string _title = "THÊM KHÁCH HÀNG";
    private string _codeDisplay = "(Tự sinh)";
    private string _name = string.Empty;
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private string _taxCode = string.Empty;
    private string _address = string.Empty;
    private string _notes = string.Empty;
    private string _occupation = string.Empty;
    private string _gender = string.Empty;
    private string? _region;
    private string? _membershipRank = "Thường";
    private string? _photoPath;
    private DateTime? _idIssueDate;
    private string _idIssuePlace = string.Empty;
    private string _bankName = string.Empty;
    private string _buyerName = string.Empty;
    private string _bankAccountNumber = string.Empty;
    private CustomerGroupOption? _selectedGroup;
    private DateTime? _birthDate;
    private DateTime? _joinDate = DateTime.Today;
    private string _openingPointsText = "0";
    private string _creditLimitText = "0";
    private bool _isActive = true;
    private string _nameError = string.Empty;
    private string _phoneError = string.Empty;
    private string _emailError = string.Empty;
    private string _groupError = string.Empty;
    private string _pointsError = string.Empty;
    private string _creditLimitError = string.Empty;
    private string _formError = string.Empty;
    private bool _isAddingGroup;
    private string _newGroupName = string.Empty;
    private string _newGroupError = string.Empty;

    public CustomerEditDialogViewModel(
        ICustomerRepository repository,
        ICurrentSessionAccessor sessionAccessor,
        long? customerId)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _customerId = customerId;

        Regions.Add(string.Empty);
        foreach (var region in CustomerViewModel.RegionOptions)
            Regions.Add(region);

        MembershipRanks.Add("Thường");
        MembershipRanks.Add("Bạc");
        MembershipRanks.Add("Vàng");
        MembershipRanks.Add("Bạch kim");
        MembershipRanks.Add("Kim cương");

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && !IsLoading);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null), () => !IsBusy);
        PickPhotoCommand = new RelayCommand(PickPhoto, () => !IsBusy && !IsLoading);
        ClearPhotoCommand = new RelayCommand(ClearPhoto, () => !IsBusy && !IsLoading && HasPhoto);
        AddGroupCommand = new RelayCommand(BeginAddGroup, () => !IsBusy && !IsLoading);
        CreateGroupCommand = new AsyncRelayCommand(CreateGroupAsync, () => !IsBusy && !IsLoading);
        CancelAddGroupCommand = new RelayCommand(CancelAddGroup, () => !IsBusy && !IsLoading);

        _ = InitializeAsync();
    }

    public event Action<CustomerEditResult?>? RequestClose;

    public ObservableCollection<CustomerGroupOption> Groups { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> MembershipRanks { get; } = new();

    public bool IsCreate => _customerId is null or 0;
    public bool IsCodeReadOnly => !IsCreate;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string CodeDisplay
    {
        get => _codeDisplay;
        private set => SetProperty(ref _codeDisplay, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string TaxCode
    {
        get => _taxCode;
        set => SetProperty(ref _taxCode, value);
    }

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string Occupation
    {
        get => _occupation;
        set => SetProperty(ref _occupation, value);
    }

    public string? PhotoPath
    {
        get => _photoPath;
        set
        {
            if (SetProperty(ref _photoPath, value))
            {
                OnPropertyChanged(nameof(HasPhoto));
                ((RelayCommand)ClearPhotoCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasPhoto => !string.IsNullOrWhiteSpace(PhotoPath);

    public string Gender
    {
        get => _gender;
        set
        {
            if (SetProperty(ref _gender, value))
            {
                OnPropertyChanged(nameof(IsGenderMale));
                OnPropertyChanged(nameof(IsGenderFemale));
                OnPropertyChanged(nameof(IsGenderOther));
            }
        }
    }

    public bool IsGenderMale
    {
        get => string.Equals(Gender, "Nam", StringComparison.OrdinalIgnoreCase);
        set { if (value) Gender = "Nam"; }
    }

    public bool IsGenderFemale
    {
        get => string.Equals(Gender, "Nữ", StringComparison.OrdinalIgnoreCase);
        set { if (value) Gender = "Nữ"; }
    }

    public bool IsGenderOther
    {
        get => string.Equals(Gender, "Khác", StringComparison.OrdinalIgnoreCase);
        set { if (value) Gender = "Khác"; }
    }

    public string? Region
    {
        get => _region;
        set => SetProperty(ref _region, value);
    }

    public string? MembershipRank
    {
        get => _membershipRank;
        set => SetProperty(ref _membershipRank, value);
    }

    public DateTime? IdIssueDate
    {
        get => _idIssueDate;
        set => SetProperty(ref _idIssueDate, value);
    }

    public string IdIssuePlace
    {
        get => _idIssuePlace;
        set => SetProperty(ref _idIssuePlace, value);
    }

    public string BankName
    {
        get => _bankName;
        set => SetProperty(ref _bankName, value);
    }

    public string BuyerName
    {
        get => _buyerName;
        set => SetProperty(ref _buyerName, value);
    }

    public string BankAccountNumber
    {
        get => _bankAccountNumber;
        set => SetProperty(ref _bankAccountNumber, value);
    }

    public CustomerGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value) && value is not null)
                GroupError = string.Empty;
        }
    }

    public bool IsAddingGroup
    {
        get => _isAddingGroup;
        private set => SetProperty(ref _isAddingGroup, value);
    }

    public string NewGroupName
    {
        get => _newGroupName;
        set
        {
            if (SetProperty(ref _newGroupName, value) && !string.IsNullOrWhiteSpace(value))
                NewGroupError = string.Empty;
        }
    }

    public string NewGroupError
    {
        get => _newGroupError;
        private set => SetProperty(ref _newGroupError, value);
    }

    public DateTime? BirthDate
    {
        get => _birthDate;
        set => SetProperty(ref _birthDate, value);
    }

    public DateTime? JoinDate
    {
        get => _joinDate;
        set => SetProperty(ref _joinDate, value);
    }

    public string OpeningPointsText
    {
        get => _openingPointsText;
        set => SetProperty(ref _openingPointsText, value);
    }

    public string CreditLimitText
    {
        get => _creditLimitText;
        set => SetProperty(ref _creditLimitText, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string NameError
    {
        get => _nameError;
        private set => SetProperty(ref _nameError, value);
    }

    public string PhoneError
    {
        get => _phoneError;
        private set => SetProperty(ref _phoneError, value);
    }

    public string EmailError
    {
        get => _emailError;
        private set => SetProperty(ref _emailError, value);
    }

    public string GroupError
    {
        get => _groupError;
        private set => SetProperty(ref _groupError, value);
    }

    public string PointsError
    {
        get => _pointsError;
        private set => SetProperty(ref _pointsError, value);
    }

    public string CreditLimitError
    {
        get => _creditLimitError;
        private set => SetProperty(ref _creditLimitError, value);
    }

    public string FormError
    {
        get => _formError;
        private set => SetProperty(ref _formError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
                ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
                ((RelayCommand)PickPhotoCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearPhotoCommand).NotifyCanExecuteChanged();
                ((RelayCommand)AddGroupCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelAddGroupCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsFormEnabled));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                CommandManager.InvalidateRequerySuggested();
                ((RelayCommand)PickPhotoCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearPhotoCommand).NotifyCanExecuteChanged();
                ((RelayCommand)AddGroupCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelAddGroupCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsFormEnabled));
            }
        }
    }

    public bool IsFormEnabled => !IsBusy && !IsLoading;

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand PickPhotoCommand { get; }
    public ICommand ClearPhotoCommand { get; }
    public ICommand AddGroupCommand { get; }
    public ICommand CreateGroupCommand { get; }
    public ICommand CancelAddGroupCommand { get; }

    private void BeginAddGroup()
    {
        NewGroupName = string.Empty;
        NewGroupError = string.Empty;
        IsAddingGroup = true;
    }

    private void CancelAddGroup()
    {
        NewGroupName = string.Empty;
        NewGroupError = string.Empty;
        IsAddingGroup = false;
    }

    private async Task CreateGroupAsync()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0)
        {
            NewGroupError = "Nhập tên nhóm khách hàng.";
            return;
        }

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            NewGroupError = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        IsBusy = true;
        NewGroupError = string.Empty;
        try
        {
            var group = await _repository.CreateGroupAsync(name, session);
            Groups.Add(group);
            SelectedGroup = group;
            IsAddingGroup = false;
            NewGroupName = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            NewGroupError = Sanitize(ex.Message);
        }
        catch (Exception)
        {
            NewGroupError = "Không thể tạo nhóm khách hàng. Vui lòng thử lại.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PickPhoto()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn ảnh khách hàng",
            Filter = "Ảnh|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Tất cả|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var previous = PhotoPath;
            var imported = CustomerPhotoStore.Import(dialog.FileName, CodeDisplay);
            PhotoPath = imported;
            if (!string.Equals(previous, imported, StringComparison.OrdinalIgnoreCase))
                CustomerPhotoStore.TryDeleteManagedFile(previous);
        }
        catch (Exception ex)
        {
            FormError = Sanitize(ex.Message);
        }
    }

    private void ClearPhoto()
    {
        var previous = PhotoPath;
        PhotoPath = null;
        CustomerPhotoStore.TryDeleteManagedFile(previous);
    }

    private async Task InitializeAsync()
    {
        IsLoading = true;
        FormError = string.Empty;
        try
        {
            Title = IsCreate ? "THÊM KHÁCH HÀNG" : "CẬP NHẬT KHÁCH HÀNG";
            var groups = await _repository.GetGroupsAsync(activeOnly: true);
            Groups.Clear();
            foreach (var group in groups)
                Groups.Add(group);

            if (!IsCreate)
            {
                var detail = await _repository.GetByIdAsync(_customerId!.Value);
                if (detail is null)
                {
                    FormError = "Khách hàng không còn tồn tại.";
                    return;
                }

                CodeDisplay = detail.Code;
                Name = detail.Name;
                Phone = detail.Phone;
                Email = detail.Email;
                TaxCode = detail.TaxCode;
                Address = detail.Address;
                Notes = detail.Notes;
                Occupation = detail.Occupation;
                Gender = detail.Gender;
                Region = detail.Region;
                MembershipRank = string.IsNullOrWhiteSpace(detail.MembershipRank) ? "Thường" : detail.MembershipRank;
                BirthDate = detail.BirthOrEstablishedDate;
                JoinDate = detail.JoinDate ?? DateTime.Today;
                OpeningPointsText = detail.OpeningPoints.ToString(CultureInfo.InvariantCulture);
                CreditLimitText = detail.CreditLimit.ToString("0.##", CultureInfo.InvariantCulture);
                IsActive = detail.IsActive;
                PhotoPath = detail.PhotoPath;
                IdIssueDate = detail.IdIssueDate;
                IdIssuePlace = detail.IdIssuePlace;
                BankName = detail.BankName;
                BuyerName = detail.BuyerName;
                BankAccountNumber = detail.BankAccountNumber;

                SelectedGroup = Groups.FirstOrDefault(g => g.Id == detail.CustomerGroupId);
                if (SelectedGroup is null && detail.CustomerGroupId > 0)
                {
                    Groups.Insert(0, new CustomerGroupOption(
                        detail.CustomerGroupId,
                        string.Empty,
                        detail.CustomerGroupName + " (không hoạt động)",
                        false,
                        false));
                    SelectedGroup = Groups[0];
                }
            }
            else
            {
                SelectedGroup = Groups.FirstOrDefault(g => g.IsDefault) ?? Groups.FirstOrDefault();
                MembershipRank = "Thường";
                JoinDate = DateTime.Today;
            }
        }
        catch (Exception)
        {
            FormError = "Không thể tải dữ liệu khách hàng.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SaveAsync()
    {
        if (!ValidateLocal())
            return;

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            FormError = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        var request = BuildRequest();
        try
        {
            var warning = await _repository.FindDuplicateWarningAsync(request);
            if (!string.IsNullOrWhiteSpace(warning))
            {
                var continueSave = MessageBox.Show(
                    warning + Environment.NewLine + Environment.NewLine + "Bạn có muốn tiếp tục lưu?",
                    "Cảnh báo trùng dữ liệu",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (continueSave != MessageBoxResult.Yes)
                    return;
            }
        }
        catch (Exception)
        {
            FormError = "Không thể kiểm tra dữ liệu trùng.";
            return;
        }

        IsBusy = true;
        FormError = string.Empty;
        try
        {
            var result = await _repository.SaveAsync(request, session);
            RequestClose?.Invoke(result);
        }
        catch (InvalidOperationException ex)
        {
            FormError = Sanitize(ex.Message);
        }
        catch (Exception)
        {
            FormError = "Không thể lưu khách hàng. Vui lòng thử lại.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateLocal()
    {
        NameError = string.Empty;
        PhoneError = string.Empty;
        EmailError = string.Empty;
        GroupError = string.Empty;
        PointsError = string.Empty;
        CreditLimitError = string.Empty;
        FormError = string.Empty;

        var ok = true;
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "Tên khách hàng là bắt buộc.";
            ok = false;
        }

        if (string.IsNullOrWhiteSpace(Phone))
        {
            PhoneError = "Số điện thoại là bắt buộc.";
            ok = false;
        }

        if (SelectedGroup is null || SelectedGroup.Id <= 0 || !SelectedGroup.IsActive)
        {
            GroupError = "Phải chọn nhóm khách hàng đang hoạt động.";
            ok = false;
        }

        if (!int.TryParse(OpeningPointsText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var points) ||
            points < 0)
        {
            PointsError = "Điểm tích lũy phải là số không âm.";
            ok = false;
        }

        if (!decimal.TryParse(CreditLimitText.Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out var credit) &&
            !decimal.TryParse(CreditLimitText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out credit) ||
            credit < 0)
        {
            CreditLimitError = "Hạn mức công nợ phải là số không âm.";
            ok = false;
        }

        var email = Email.Trim();
        if (email.Length > 0 && !EmailRegex.IsMatch(email))
        {
            EmailError = "Email không đúng định dạng.";
            ok = false;
        }

        return ok;
    }

    private CustomerSaveRequest BuildRequest()
    {
        int.TryParse(OpeningPointsText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var points);
        if (!decimal.TryParse(CreditLimitText.Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out var credit))
            decimal.TryParse(CreditLimitText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out credit);

        return new CustomerSaveRequest(
            _customerId,
            Name,
            SelectedGroup?.Id ?? 0,
            Address,
            Phone,
            Email,
            BirthDate,
            points,
            TaxCode,
            Notes,
            Gender,
            Region ?? string.Empty,
            Occupation,
            MembershipRank ?? string.Empty,
            JoinDate,
            credit,
            IsActive,
            string.IsNullOrWhiteSpace(PhotoPath) ? null : PhotoPath.Trim(),
            IdIssueDate,
            IdIssuePlace,
            BankName,
            BuyerName,
            BankAccountNumber);
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Không thể lưu khách hàng.";
        if (message.Contains("SQL", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Firebird", StringComparison.OrdinalIgnoreCase) ||
            message.Contains('\\') ||
            message.Contains(".fdb", StringComparison.OrdinalIgnoreCase))
            return "Không thể lưu khách hàng. Vui lòng thử lại.";
        return message.Length > 300 ? message[..300] + "…" : message;
    }
}
