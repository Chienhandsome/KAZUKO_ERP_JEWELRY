using System.Collections.ObjectModel;

namespace Kazuko.Modules.Reports;

internal static class ReportCatalog
{
    public static ObservableCollection<ReportTreeNode> CreateMenu() =>
    [
        ReportTreeNode.Group("BÁO CÁO NHÂN SỰ",
            ReportTreeNode.Report("Bảng lương tháng", "reports.hr.payroll"),
            ReportTreeNode.Report("Phiếu lương cá nhân", "reports.hr.payroll_slip")),
        ReportTreeNode.Group(
            "BÁO CÁO QUỸ",
            ReportTreeNode.Report("Báo cáo tồn quỹ", "reports.fund.balance"),
            ReportTreeNode.Report("Báo cáo quỹ tiền mặt", "reports.fund.cash"),
            ReportTreeNode.Report("Danh sách phiếu thu tiền mặt", "reports.fund.cash_receipts"),
            ReportTreeNode.Report("Danh sách phiếu chi tiền mặt", "reports.fund.cash_payments")),
        ReportTreeNode.Group(
            "BÁO CÁO DANH MỤC",
            ReportTreeNode.Report("Danh mục khách hàng", "reports.master.customers"),
            ReportTreeNode.Report("Danh mục nhà cung cấp", "reports.master.suppliers")),
        ReportTreeNode.Group(
            "BÁO CÁO BÁN HÀNG",
            ReportTreeNode.Report("Danh sách hóa đơn bán hàng", "reports.sales.invoices"),
            ReportTreeNode.Report("Tổng hợp lãi lỗ", "reports.sales.profit_loss"),
            ReportTreeNode.Report("Tổng hợp hóa đơn bán hàng theo ngày", "reports.sales.invoices_by_day"),
            ReportTreeNode.Report("Chi tiết bán hàng theo ngày", "reports.sales.detail_by_day"),
            ReportTreeNode.Report("Chi tiết bán vàng mới theo loại vàng", "reports.sales.new_gold_detail_by_type"),
            ReportTreeNode.Report("Chi tiết mua vàng cũ theo loại vàng", "reports.sales.old_gold_purchase_detail_by_type"),
            ReportTreeNode.Report("Tổng hợp bán vàng mới theo loại vàng", "reports.sales.new_gold_summary_by_type"),
            ReportTreeNode.Report("Tổng hợp mua vàng cũ theo loại vàng", "reports.sales.old_gold_purchase_summary_by_type"),
            ReportTreeNode.Report("Báo cáo 20 mặt hàng bán chạy nhất", "reports.sales.top20_items")),
        ReportTreeNode.Group(
            "BÁO CÁO KHO HÀNG",
            ReportTreeNode.Report("Chi tiết nhập hàng đã xét duyệt", "reports.warehouse.approved_receipts"),
            ReportTreeNode.Report("Chi tiết nhập hàng chưa xét duyệt", "reports.warehouse.pending_receipts"),
            ReportTreeNode.Report("Báo cáo nữ trang tồn kho", "reports.warehouse.jewelry_on_hand"),
            ReportTreeNode.Report("Tổng hợp nhập hàng theo loại", "reports.warehouse.receipts_by_type"),
            ReportTreeNode.Report("Tổng hợp nữ trang tồn kho", "reports.warehouse.jewelry_on_hand_summary"))
    ];
}
