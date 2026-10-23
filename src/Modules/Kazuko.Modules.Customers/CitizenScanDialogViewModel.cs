using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Customers;

public sealed record CitizenScanFormSnapshot(string Name, string Number, DateTime? BirthDate,
    string Gender, string Address, DateTime? IssueDate);
public sealed class CitizenFieldChange(string field, string current, string scanned, Action onChanged) : ObservableObject
{
    private string _scanned = scanned;
    public string Field { get; } = field;
    public string Current { get; } = current;
    public string Scanned
    {
        get => _scanned;
        set { if (SetProperty(ref _scanned, value)) onChanged(); }
    }
}
public sealed record CitizenScanOutcome(CitizenIdentityScanData Data, CustomerIdentityMatch? Existing = null, bool EditExisting = false);

public sealed class CitizenScanDialogViewModel : ObservableObject
{
    private readonly ICustomerRepository _repository;
    private readonly AuthenticatedSession _session;
    private readonly long? _excludeId;
    private readonly CitizenScanFormSnapshot _current;
    private readonly bool _canEdit;
    private string _raw = "", _error = "", _status = "Đang chờ máy quét iMarcone hoặc dữ liệu dán.";
    private bool _busy, _closed, _confirm;
    private int _revision;
    private CitizenIdentityScanData? _data;
    private CustomerIdentityMatch? _selected;

    public CitizenScanDialogViewModel(ICustomerRepository repository, AuthenticatedSession session, long? excludeId,
        CitizenScanFormSnapshot current, bool canEdit)
    {
        _repository = repository; _session = session; _excludeId = excludeId; _current = current; _canEdit = canEdit;
        ReadCommand = new AsyncRelayCommand(ReadAsync, () => !IsBusy && !_closed);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsBusy && !_closed && Data is not null && !HasMatches &&
            (!RequiresIdentityConfirmation || ConfirmIdentityReplacement));
        UseExistingCommand = new AsyncRelayCommand(() => ChooseExistingAsync(false), () => !IsBusy && !_closed &&
            SelectedMatch is { IsActive: true, IsWalkIn: false });
        EditExistingCommand = new AsyncRelayCommand(() => ChooseExistingAsync(true), () => !IsBusy && !_closed &&
            _canEdit && SelectedMatch is { IsWalkIn: false });
        CancelCommand = new RelayCommand(Cancel);
    }

    public event Action? RequestClose;
    public CitizenScanOutcome? Outcome { get; private set; }
    public ObservableCollection<CustomerIdentityMatch> Matches { get; } = new();
    public ObservableCollection<CitizenFieldChange> Changes { get; } = new();
    public string RawInput
    {
        get => _raw;
        set
        {
            if (!SetProperty(ref _raw, value)) return;
            _revision++; Data = null; Matches.Clear(); Changes.Clear(); SelectedMatch = null;
            ConfirmIdentityReplacement = false; Error = ""; Status = "Đang chờ đọc chuỗi quét.";
            Notify();
        }
    }
    public CitizenIdentityScanData? Data { get => _data; private set { SetProperty(ref _data, value); Notify(); } }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Notify(); OnPropertyChanged(nameof(CanReadInput)); } }
    public bool CanReadInput => !IsBusy;
    public bool HasMatches => Matches.Count > 0;
    public bool HasData => Data is not null;
    public bool RequiresIdentityConfirmation => Data is not null && !string.IsNullOrWhiteSpace(_current.Number) &&
        _current.Number.Trim() != Changes.FirstOrDefault()?.Scanned.Trim() && _current.Number.Trim() != Data.PreviousNumber;
    public bool ConfirmIdentityReplacement { get => _confirm; set { SetProperty(ref _confirm, value); Notify(); } }
    public CustomerIdentityMatch? SelectedMatch
    {
        get => _selected;
        set { SetProperty(ref _selected, value); OnPropertyChanged(nameof(SelectedStatus)); Notify(); }
    }
    public string SelectedStatus => SelectedMatch?.StatusText ?? "";
    public ICommand ReadCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand UseExistingCommand { get; }
    public ICommand EditExistingCommand { get; }
    public ICommand CancelCommand { get; }

    public Task ReadAsync()
    {
        if (_closed || IsBusy) return Task.CompletedTask;
        var parsed = CitizenIdentityQrParser.Parse(RawInput, DateTime.Today);
        if (!parsed.Success)
        {
            Data = null; Matches.Clear(); Changes.Clear(); SelectedMatch = null;
            ConfirmIdentityReplacement = false; Notify(); Error = parsed.Error; return Task.CompletedTask;
        }
        return LoadDataAsync(parsed.Data!);
    }

    public async Task LoadDataAsync(CitizenIdentityScanData data)
    {
        if (_closed || IsBusy) return;
        int revision = _revision;
        IsBusy = true; Error = ""; Data = null; Matches.Clear(); Changes.Clear(); SelectedMatch = null;
        try
        {
            var matches = await _repository.FindIdentityMatchesAsync(data.Number, data.PreviousNumber, _excludeId, _session);
            if (_closed || revision != _revision) return;
            Data = data;
            foreach (var match in matches) Matches.Add(match);
            SelectedMatch = Matches.FirstOrDefault();
            AddChange("CCCD", _current.Number, data.Number);
            AddChange("Họ tên", _current.Name, data.FullName);
            AddChange("Ngày sinh", _current.BirthDate?.ToString("dd/MM/yyyy"), data.BirthDate.ToString("dd/MM/yyyy"));
            AddChange("Giới tính", _current.Gender, data.Gender);
            AddChange("Địa chỉ", _current.Address, data.Address);
            AddChange("Ngày cấp", _current.IssueDate?.ToString("dd/MM/yyyy"), data.IssueDate?.ToString("dd/MM/yyyy"));
            _raw = ""; OnPropertyChanged(nameof(RawInput));
            Status = HasMatches ? "Đã có hồ sơ khớp CCCD/CMND. Chọn khách hiện có; không tạo hồ sơ trùng."
                : "Có thể sửa trực tiếp cột Từ CCCD rồi áp dụng; SĐT và nơi cấp giữ nguyên trên form.";
        }
        catch { if (!_closed) Error = "Không thể đối chiếu hồ sơ khách hàng. Vui lòng thử lại."; }
        finally { IsBusy = false; }
    }

    private void AddChange(string field, string? current, string? scanned) =>
        Changes.Add(new(field, string.IsNullOrEmpty(current) ? "—" : current, scanned ?? "", () => ReviewChanged(field)));

    private void ReviewChanged(string field)
    {
        _revision++;
        Matches.Clear(); SelectedMatch = null; Error = "";
        if (field == "CCCD") ConfirmIdentityReplacement = false;
        Status = "Đã sửa thông tin. Bấm Áp dụng để kiểm tra dữ liệu và đối chiếu CCCD/CMND lại.";
        Notify();
    }

    private CitizenIdentityScanData? ValidateReview()
    {
        if (Data is null || Changes.Count != 6) return null;
        string Value(int index) => Changes[index].Scanned.Trim();
        if (!DateTime.TryParseExact(Value(2), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
        { Error = "Ngày sinh không hợp lệ. Nhập theo dd/MM/yyyy."; return null; }
        DateTime? issue = null;
        if (Value(5).Length > 0)
        {
            if (!DateTime.TryParseExact(Value(5), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            { Error = "Ngày cấp không hợp lệ. Nhập theo dd/MM/yyyy hoặc để trống để giữ nguyên."; return null; }
            issue = date;
        }
        // Reuse the same validation as scanning, including Unicode, lengths and date bounds.
        var result = CitizenIdentityQrParser.Parse(string.Join("|", Value(0), Data.PreviousNumber, Value(1),
            birth.ToString("ddMMyyyy", CultureInfo.InvariantCulture), Value(3), Value(4),
            issue?.ToString("ddMMyyyy", CultureInfo.InvariantCulture) ?? ""), DateTime.Today);
        if (!result.Success) Error = result.Error;
        return result.Data;
    }

    public async Task ApplyAsync()
    {
        if (IsBusy || _closed || Data is null || HasMatches ||
            (RequiresIdentityConfirmation && !ConfirmIdentityReplacement)) return;
        var corrected = ValidateReview();
        if (corrected is null) return;
        int revision = _revision;
        IsBusy = true; Error = "";
        try
        {
            var matches = await _repository.FindIdentityMatchesAsync(corrected.Number, corrected.PreviousNumber, _excludeId, _session);
            if (_closed || revision != _revision) return;
            Data = corrected;
            Matches.Clear();
            foreach (var match in matches) Matches.Add(match);
            SelectedMatch = Matches.FirstOrDefault();
            if (HasMatches)
            {
                Status = "Số giấy tờ sau khi sửa đã có hồ sơ. Chọn khách hiện có; không tạo hồ sơ trùng.";
                return;
            }
            Outcome = new(corrected); Close();
        }
        catch { if (!_closed && revision == _revision) Error = "Không thể đối chiếu hồ sơ khách hàng. Vui lòng thử lại."; }
        finally { IsBusy = false; }
    }

    public async Task ChooseExistingAsync(bool edit)
    {
        if (IsBusy || _closed || Data is null || SelectedMatch is null || (edit && !_canEdit)) return;
        var data = Data;
        int revision = _revision;
        IsBusy = true; Error = "";
        try
        {
            long id = SelectedMatch.Id;
            var matches = await _repository.FindIdentityMatchesAsync(data.Number, data.PreviousNumber, _excludeId, _session);
            if (_closed || revision != _revision) return;
            var match = matches.SingleOrDefault(item => item.Id == id);
            if (match is null || match.IsWalkIn || (!edit && !match.IsActive))
            { Error = "Hồ sơ không còn phù hợp hoặc đã ngừng hoạt động. Vui lòng đọc lại thông tin."; return; }
            Outcome = new(data, match, edit); Close();
        }
        catch { if (!_closed) Error = "Không thể chọn hồ sơ khách hàng. Vui lòng thử lại."; }
        finally { IsBusy = false; }
    }

    public void Cancel() { Outcome = null; Close(); }
    public void OnClosed() { _closed = true; _raw = ""; }
    private void Close() { _closed = true; _raw = ""; RequestClose?.Invoke(); }
    private void Notify()
    {
        OnPropertyChanged(nameof(HasMatches)); OnPropertyChanged(nameof(HasData)); OnPropertyChanged(nameof(RequiresIdentityConfirmation));
        (ReadCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ApplyCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (UseExistingCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (EditExistingCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }
}
