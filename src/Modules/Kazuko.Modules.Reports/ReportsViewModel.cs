using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public sealed class ReportsViewModel : ObservableObject
{
    public const string JewelryOnHandKey = "reports.warehouse.jewelry_on_hand";
    public const string JewelryOnHandSummaryKey = "reports.warehouse.jewelry_on_hand_summary";
    public const string FundBalanceKey = "reports.fund.balance";
    public const string FundCashKey = "reports.fund.cash";
    public const string FundCashReceiptsKey = "reports.fund.cash_receipts";
    public const string FundCashPaymentsKey = "reports.fund.cash_payments";
    public const string MasterCustomersKey = "reports.master.customers";
    public const string MasterSuppliersKey = "reports.master.suppliers";
    public const string SalesInvoicesKey = "reports.sales.invoices";
    public const string SalesProfitLossKey = "reports.sales.profit_loss";
    public const string SalesInvoicesByDayKey = "reports.sales.invoices_by_day";
    public const string SalesDetailByDayKey = "reports.sales.detail_by_day";
    public const string SalesNewGoldDetailByTypeKey = "reports.sales.new_gold_detail_by_type";
    public const string SalesOldGoldPurchaseDetailByTypeKey = "reports.sales.old_gold_purchase_detail_by_type";
    public const string SalesNewGoldSummaryByTypeKey = "reports.sales.new_gold_summary_by_type";
    public const string SalesOldGoldPurchaseSummaryByTypeKey = "reports.sales.old_gold_purchase_summary_by_type";
    public const string SalesTopItemsKey = "reports.sales.top20_items";
    public const string WarehouseApprovedReceiptsKey = "reports.warehouse.approved_receipts";
    public const string WarehousePendingReceiptsKey = "reports.warehouse.pending_receipts";
    public const string WarehouseReceiptsByTypeKey = "reports.warehouse.receipts_by_type";
    public const string PayrollKey = "reports.hr.payroll";
    public const string PayrollSlipKey = "reports.hr.payroll_slip";

    private ReportTreeNode? _selectedNode;
    private string _placeholderTitle = "Báo cáo";
    private string _placeholderMessage = "Chọn một báo cáo trong danh sách bên trái.";
    private bool _showPlaceholder = true;
    private ReportWorkspaceViewModel? _activeWorkspace;
    private bool _isCatalogVisible = true;

    public ReportsViewModel(
        JewelryOnHandReportViewModel jewelryOnHand,
        JewelryOnHandSummaryReportViewModel jewelryOnHandSummary,
        FundBalanceReportViewModel fundBalance,
        FundCashReportViewModel fundCash,
        FundCashReceiptsReportViewModel fundCashReceipts,
        FundCashPaymentsReportViewModel fundCashPayments,
        MasterCustomersReportViewModel masterCustomers,
        MasterSuppliersReportViewModel masterSuppliers,
        SalesInvoicesReportViewModel salesInvoices,
        SalesProfitLossReportViewModel salesProfitLoss,
        SalesInvoicesByDayReportViewModel salesInvoicesByDay,
        SalesDetailByDayReportViewModel salesDetailByDay,
        SalesNewGoldDetailByTypeReportViewModel salesNewGoldDetailByType,
        SalesOldGoldPurchaseDetailByTypeReportViewModel salesOldGoldPurchaseDetailByType,
        SalesNewGoldSummaryByTypeReportViewModel salesNewGoldSummaryByType,
        SalesOldGoldPurchaseSummaryByTypeReportViewModel salesOldGoldPurchaseSummaryByType,
        SalesTopItemsReportViewModel salesTopItems,
        WarehouseApprovedReceiptsReportViewModel warehouseApprovedReceipts,
        WarehousePendingReceiptsReportViewModel warehousePendingReceipts,
        WarehouseReceiptsByTypeReportViewModel warehouseReceiptsByType,
        PayrollReportViewModel? payroll = null, PayrollReportViewModel? payrollSlip = null)
    {
        JewelryOnHand = jewelryOnHand;
        JewelryOnHandSummary = jewelryOnHandSummary;
        FundBalance = fundBalance;
        FundCash = fundCash;
        FundCashReceipts = fundCashReceipts;
        FundCashPayments = fundCashPayments;
        MasterCustomers = masterCustomers;
        MasterSuppliers = masterSuppliers;
        SalesInvoices = salesInvoices;
        SalesProfitLoss = salesProfitLoss;
        SalesInvoicesByDay = salesInvoicesByDay;
        SalesDetailByDay = salesDetailByDay;
        SalesNewGoldDetailByType = salesNewGoldDetailByType;
        SalesOldGoldPurchaseDetailByType = salesOldGoldPurchaseDetailByType;
        SalesNewGoldSummaryByType = salesNewGoldSummaryByType;
        SalesOldGoldPurchaseSummaryByType = salesOldGoldPurchaseSummaryByType;
        SalesTopItems = salesTopItems;
        WarehouseApprovedReceipts = warehouseApprovedReceipts;
        WarehousePendingReceipts = warehousePendingReceipts;
        WarehouseReceiptsByType = warehouseReceiptsByType;
        Payroll = payroll; PayrollSlip = payrollSlip;
        Menu = ReportCatalog.CreateMenu();
        ToggleCatalogCommand = new RelayCommand(ToggleCatalog);
    }

    public ObservableCollection<ReportTreeNode> Menu { get; }
    public JewelryOnHandReportViewModel JewelryOnHand { get; }
    public JewelryOnHandSummaryReportViewModel JewelryOnHandSummary { get; }
    public FundBalanceReportViewModel FundBalance { get; }
    public FundCashReportViewModel FundCash { get; }
    public FundCashReceiptsReportViewModel FundCashReceipts { get; }
    public FundCashPaymentsReportViewModel FundCashPayments { get; }
    public MasterCustomersReportViewModel MasterCustomers { get; }
    public MasterSuppliersReportViewModel MasterSuppliers { get; }
    public SalesInvoicesReportViewModel SalesInvoices { get; }
    public SalesProfitLossReportViewModel SalesProfitLoss { get; }
    public SalesInvoicesByDayReportViewModel SalesInvoicesByDay { get; }
    public SalesDetailByDayReportViewModel SalesDetailByDay { get; }
    public SalesNewGoldDetailByTypeReportViewModel SalesNewGoldDetailByType { get; }
    public SalesOldGoldPurchaseDetailByTypeReportViewModel SalesOldGoldPurchaseDetailByType { get; }
    public SalesNewGoldSummaryByTypeReportViewModel SalesNewGoldSummaryByType { get; }
    public SalesOldGoldPurchaseSummaryByTypeReportViewModel SalesOldGoldPurchaseSummaryByType { get; }
    public SalesTopItemsReportViewModel SalesTopItems { get; }
    public WarehouseApprovedReceiptsReportViewModel WarehouseApprovedReceipts { get; }
    public WarehousePendingReceiptsReportViewModel WarehousePendingReceipts { get; }
    public WarehouseReceiptsByTypeReportViewModel WarehouseReceiptsByType { get; }
    public PayrollReportViewModel? Payroll { get; }
    public PayrollReportViewModel? PayrollSlip { get; }
    public ICommand ToggleCatalogCommand { get; }

    public ReportTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
                return;

            UpdateContent();
        }
    }

    public bool IsCatalogVisible
    {
        get => _isCatalogVisible;
        private set
        {
            if (!SetProperty(ref _isCatalogVisible, value))
                return;

            OnPropertyChanged(nameof(CatalogToggleLabel));
        }
    }

    public string CatalogToggleLabel => IsCatalogVisible ? "Ẩn danh mục" : "Hiện danh mục";

    public string PlaceholderTitle
    {
        get => _placeholderTitle;
        private set => SetProperty(ref _placeholderTitle, value);
    }

    public string PlaceholderMessage
    {
        get => _placeholderMessage;
        private set => SetProperty(ref _placeholderMessage, value);
    }

    public bool ShowPlaceholder
    {
        get => _showPlaceholder;
        private set => SetProperty(ref _showPlaceholder, value);
    }

    /// <summary>Workspace báo cáo đang mở; null = placeholder. Đổi instance để WebView2 không đè HWND.</summary>
    public ReportWorkspaceViewModel? ActiveWorkspace
    {
        get => _activeWorkspace;
        private set
        {
            if (!SetProperty(ref _activeWorkspace, value))
                return;
            OnPropertyChanged(nameof(ShowWorkspace));
        }
    }

    public bool ShowWorkspace => ActiveWorkspace is not null;

    private void ToggleCatalog() => IsCatalogVisible = !IsCatalogVisible;

    private void UpdateContent()
    {
        if (SelectedNode is null)
        {
            ActiveWorkspace = null;
            PlaceholderTitle = "Báo cáo";
            PlaceholderMessage = "Chọn một báo cáo trong danh sách bên trái.";
            ShowPlaceholder = true;
            return;
        }

        if (SelectedNode.IsReport)
        {
            var workspace = SelectedNode.ReportKey switch
            {
                var k when k == JewelryOnHandKey => (ReportWorkspaceViewModel)JewelryOnHand,
                var k when k == JewelryOnHandSummaryKey => JewelryOnHandSummary,
                var k when k == FundBalanceKey => FundBalance,
                var k when k == FundCashKey => FundCash,
                var k when k == FundCashReceiptsKey => FundCashReceipts,
                var k when k == FundCashPaymentsKey => FundCashPayments,
                var k when k == MasterCustomersKey => MasterCustomers,
                var k when k == MasterSuppliersKey => MasterSuppliers,
                var k when k == SalesInvoicesKey => SalesInvoices,
                var k when k == SalesProfitLossKey => SalesProfitLoss,
                var k when k == SalesInvoicesByDayKey => SalesInvoicesByDay,
                var k when k == SalesDetailByDayKey => SalesDetailByDay,
                var k when k == SalesNewGoldDetailByTypeKey => SalesNewGoldDetailByType,
                var k when k == SalesOldGoldPurchaseDetailByTypeKey => SalesOldGoldPurchaseDetailByType,
                var k when k == SalesNewGoldSummaryByTypeKey => SalesNewGoldSummaryByType,
                var k when k == SalesOldGoldPurchaseSummaryByTypeKey => SalesOldGoldPurchaseSummaryByType,
                var k when k == SalesTopItemsKey => SalesTopItems,
                var k when k == WarehouseApprovedReceiptsKey => WarehouseApprovedReceipts,
                var k when k == WarehousePendingReceiptsKey => WarehousePendingReceipts,
                var k when k == WarehouseReceiptsByTypeKey => WarehouseReceiptsByType,
                var k when k == PayrollKey => Payroll,
                var k when k == PayrollSlipKey => PayrollSlip,
                _ => null
            };

            if (workspace is not null)
            {
                ShowPlaceholder = false;
                ActiveWorkspace = workspace;
                _ = workspace.EnsureLoadedAsync();
                return;
            }
        }

        ActiveWorkspace = null;
        ShowPlaceholder = true;
        PlaceholderTitle = SelectedNode.Title;
        PlaceholderMessage = SelectedNode.IsReport
            ? "Báo cáo này đang được xây dựng. Dữ liệu, bộ lọc và xuất file sẽ được bổ sung trong các bản cập nhật tiếp theo."
            : "Mở mục con và chọn báo cáo cụ thể.";
    }
}
