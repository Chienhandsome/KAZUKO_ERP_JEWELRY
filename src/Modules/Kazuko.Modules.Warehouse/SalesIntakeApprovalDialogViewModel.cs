using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class SalesIntakeApprovalDialogViewModel : ObservableObject
{
    private readonly IFinishedProductTransferService _service;
    private readonly AuthenticatedSession _session;
    private SalesIntakeRequestSummary? _selectedRequest;
    private string _statusFilter = "PENDING";
    private string _rejectReason = string.Empty;
    private string _errorMessage = string.Empty;
    private string _detailCode = string.Empty;
    private string _detailStatus = string.Empty;
    private string _detailCreatedBy = string.Empty;
    private string _detailCreatedAt = string.Empty;
    private string _detailSource = string.Empty;
    private string _detailDestination = string.Empty;
    private string _detailNotes = string.Empty;
    private string _detailRejectionReason = string.Empty;
    private bool _hasDetail;
    private bool _canDecide;
    private bool _isBusy;
    private bool? _dialogResult;

    public SalesIntakeApprovalDialogViewModel(
        IFinishedProductTransferService service,
        AuthenticatedSession session)
    {
        _service = service;
        _session = session;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => CanDecide && !IsBusy);
        RejectCommand = new AsyncRelayCommand(RejectAsync, () => CanDecide && !IsBusy);
        CloseCommand = new RelayCommand(() => DialogResult = false);
        _ = RefreshAsync();
    }

    public ObservableCollection<string> StatusFilterOptions { get; } =
    [
        "PENDING",
        "APPROVED",
        "REJECTED",
        "Tất cả"
    ];

    public ObservableCollection<SalesIntakeRequestSummary> Requests { get; } = new();
    public ObservableCollection<SalesIntakeRequestLine> DetailLines { get; } = new();

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (SetProperty(ref _statusFilter, value))
                _ = RefreshAsync();
        }
    }

    public SalesIntakeRequestSummary? SelectedRequest
    {
        get => _selectedRequest;
        set
        {
            if (SetProperty(ref _selectedRequest, value))
                _ = LoadDetailAsync();
        }
    }

    public string RejectReason
    {
        get => _rejectReason;
        set => SetProperty(ref _rejectReason, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string DetailCode
    {
        get => _detailCode;
        set => SetProperty(ref _detailCode, value);
    }

    public string DetailStatus
    {
        get => _detailStatus;
        set => SetProperty(ref _detailStatus, value);
    }

    public string DetailCreatedBy
    {
        get => _detailCreatedBy;
        set => SetProperty(ref _detailCreatedBy, value);
    }

    public string DetailCreatedAt
    {
        get => _detailCreatedAt;
        set => SetProperty(ref _detailCreatedAt, value);
    }

    public string DetailSource
    {
        get => _detailSource;
        set => SetProperty(ref _detailSource, value);
    }

    public string DetailDestination
    {
        get => _detailDestination;
        set => SetProperty(ref _detailDestination, value);
    }

    public string DetailNotes
    {
        get => _detailNotes;
        set => SetProperty(ref _detailNotes, value);
    }

    public string DetailRejectionReason
    {
        get => _detailRejectionReason;
        set => SetProperty(ref _detailRejectionReason, value);
    }

    public bool HasDetail
    {
        get => _hasDetail;
        set => SetProperty(ref _hasDetail, value);
    }

    public bool CanDecide
    {
        get => _canDecide;
        private set
        {
            if (SetProperty(ref _canDecide, value))
            {
                (ApproveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (RejectCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                (RefreshCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (ApproveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (RejectCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        set => SetProperty(ref _dialogResult, value);
    }

    public bool Changed { get; private set; }

    public ICommand RefreshCommand { get; }
    public ICommand ApproveCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand CloseCommand { get; }

    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var status = string.Equals(StatusFilter, "Tất cả", StringComparison.Ordinal)
                ? null
                : StatusFilter;
            var rows = await _service.ListRequestsAsync(status, _session);
            var selectedId = SelectedRequest?.Id;
            Requests.Clear();
            foreach (var row in rows)
                Requests.Add(row);

            SelectedRequest = selectedId is > 0
                ? Requests.FirstOrDefault(x => x.Id == selectedId) ?? Requests.FirstOrDefault()
                : Requests.FirstOrDefault();

            if (SelectedRequest is null)
                ClearDetail();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Duyệt nhập từ kho TP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDetailAsync()
    {
        if (SelectedRequest is null)
        {
            ClearDetail();
            return;
        }

        try
        {
            var detail = await _service.GetRequestDetailAsync(SelectedRequest.Id, _session);
            if (detail is null)
            {
                ClearDetail();
                ErrorMessage = "Không tải được chi tiết yêu cầu.";
                return;
            }

            DetailCode = detail.Code;
            DetailStatus = detail.StatusCode;
            DetailCreatedBy = detail.CreatedByName;
            DetailCreatedAt = detail.CreatedAt.ToString("dd/MM/yyyy HH:mm");
            DetailSource = detail.SourceLocationName;
            DetailDestination = detail.DestinationLocationName;
            DetailNotes = detail.Notes;
            DetailRejectionReason = detail.RejectionReason;
            DetailLines.Clear();
            foreach (var line in detail.Lines)
                DetailLines.Add(line);
            HasDetail = true;
            CanDecide = string.Equals(detail.StatusCode, SalesIntakeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase);
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ClearDetail();
            ErrorMessage = ex.Message;
        }
    }

    private void ClearDetail()
    {
        DetailCode = string.Empty;
        DetailStatus = string.Empty;
        DetailCreatedBy = string.Empty;
        DetailCreatedAt = string.Empty;
        DetailSource = string.Empty;
        DetailDestination = string.Empty;
        DetailNotes = string.Empty;
        DetailRejectionReason = string.Empty;
        DetailLines.Clear();
        HasDetail = false;
        CanDecide = false;
    }

    private async Task ApproveAsync()
    {
        if (SelectedRequest is null || !CanDecide)
            return;

        var confirm = MessageBox.Show(
            $"Đồng ý nhập phiếu {SelectedRequest.Code} vào Kho bán hàng?",
            "Duyệt nhập từ kho TP",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await _service.ApproveAsync(SelectedRequest.Id, _session);
            Changed = true;
            RejectReason = string.Empty;
            await RefreshInternalAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Duyệt nhập từ kho TP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RejectAsync()
    {
        if (SelectedRequest is null || !CanDecide)
            return;

        if (string.IsNullOrWhiteSpace(RejectReason))
        {
            ErrorMessage = "Nhập lý do từ chối.";
            MessageBox.Show(ErrorMessage, "Duyệt nhập từ kho TP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Từ chối phiếu {SelectedRequest.Code}? Hàng vẫn giữ ở Kho thành phẩm.",
            "Duyệt nhập từ kho TP",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            await _service.RejectAsync(SelectedRequest.Id, RejectReason, _session);
            Changed = true;
            RejectReason = string.Empty;
            await RefreshInternalAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show(ex.Message, "Duyệt nhập từ kho TP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshInternalAsync()
    {
        var status = string.Equals(StatusFilter, "Tất cả", StringComparison.Ordinal)
            ? null
            : StatusFilter;
        var rows = await _service.ListRequestsAsync(status, _session);
        Requests.Clear();
        foreach (var row in rows)
            Requests.Add(row);
        SelectedRequest = Requests.FirstOrDefault();
        if (SelectedRequest is null)
            ClearDetail();
    }
}
