using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Warehouse;

public sealed class ProductionFinishedReceiptDialogViewModel : ObservableObject
{
    private readonly IProductionQueryRepository _queries;
    private readonly IProductionApplicationService _application;
    private readonly AuthenticatedSession _session;
    private ProductionFinishedReceiptOrderOption? _selectedOrder;
    private ProductionOrderDetail? _detail;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;

    public ProductionFinishedReceiptDialogViewModel(
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        AuthenticatedSession session)
    {
        _queries = queries;
        _application = application;
        _session = session;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        _ = LoadOrdersAsync();
    }

    public ObservableCollection<ProductionFinishedReceiptOrderOption> Orders { get; } = [];
    public ObservableCollection<ProductionQcUnitDto> Products { get; } = [];

    public ProductionFinishedReceiptOrderOption? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
                _ = LoadDetailAsync(value);
        }
    }

    public string OrderCode => _detail?.Code ?? string.Empty;
    public string ProductionType => _detail is null ? string.Empty
        : _detail.OrderSource == ProductionOrderSources.SalesOrder
            ? "Chế tác theo đơn" : "Chế tác nhập kho";
    public string SalesOrderCode => _detail?.SalesOrderCode ?? "—";
    public string ProductionDate => _detail?.OrderDate.ToString("dd/MM/yyyy") ?? string.Empty;
    public string Supplier => _detail is null ? string.Empty
        : _detail.ExecutionType == ProductionExecutionTypes.Outsourced
            ? $"Xưởng chế tác ngoài: {_detail.ExternalWorkerName}" : "Nội bộ";
    public string FinishedLocation => _detail?.FinishedLocationName ?? string.Empty;
    public string ProductCount => $"{Products.Count} món";
    public string TotalGoldWeight => $"{Products.Sum(x => x.ActualGoldWeight ?? 0):0.###} chỉ";
    public string Notes => _detail?.Notes ?? string.Empty;
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool? DialogResult { get => _dialogResult; private set => SetProperty(ref _dialogResult, value); }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task LoadOrdersAsync()
    {
        try
        {
            var items = await _queries.GetCompletedUnreceivedOrdersAsync(_session.BranchId);
            Orders.Clear();
            foreach (var item in items)
                Orders.Add(item);
            SelectedOrder = Orders.FirstOrDefault();
            if (Orders.Count == 0)
                ErrorMessage = "Không có lệnh đã hoàn thành đang chờ nhập kho.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task LoadDetailAsync(ProductionFinishedReceiptOrderOption? item)
    {
        try
        {
            ErrorMessage = string.Empty;
            _detail = null;
            Products.Clear();
            NotifyDetailChanged();
            if (item is not null)
            {
                var detail = await _queries.GetOrderDetailAsync(item.Id, _session.BranchId);
                if (SelectedOrder?.Id != item.Id)
                    return;
                _detail = detail;
            }
            if (_detail is not null)
                foreach (var unit in _detail.QcUnits)
                    Products.Add(unit);
            NotifyDetailChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void NotifyDetailChanged()
    {
        foreach (var name in new[] { nameof(OrderCode), nameof(ProductionType), nameof(SalesOrderCode),
                     nameof(ProductionDate), nameof(Supplier), nameof(FinishedLocation),
                     nameof(ProductCount), nameof(TotalGoldWeight), nameof(Notes) })
            OnPropertyChanged(name);
    }

    private async Task SaveAsync()
    {
        if (_detail is null || _detail.FinishedReceiptDocumentId is not null)
        {
            ErrorMessage = "Chọn lệnh sản xuất đã hoàn thành và chưa nhập kho.";
            return;
        }
        try
        {
            ErrorMessage = string.Empty;
            await _application.PostFinishedReceiptAsync(_detail.Id, _detail.RowVersion, _session);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
