using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

/// <summary>
/// Báo cáo "Chi tiết nhập hàng đã xét duyệt" (reports.warehouse.approved_receipts):
/// từng dòng phiếu nhập kho đã ghi sổ (STATUS_CODE = 'POSTED'), lọc theo ngày phiếu.
/// Schema hiện tại không có trạng thái duyệt riêng — 'POSTED' là điều kiện gần nhất với "đã xét duyệt".
/// </summary>
public sealed record WarehouseApprovedReceiptsReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record WarehouseApprovedReceiptsReportRow(
    long LineId,
    DateTime DocumentDate,
    string DocumentCode,
    string MaterialCode,
    string MaterialName,
    string GoldTypeName,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal AmountVnd,
    string SupplierName);

public sealed record WarehouseApprovedReceiptsReportResult(
    IReadOnlyList<WarehouseApprovedReceiptsReportRow> Rows,
    int DocumentCount,
    int LineCount,
    decimal TotalQuantity,
    decimal TotalAmountVnd);

public interface IWarehouseApprovedReceiptsReportService
{
    Task<WarehouseApprovedReceiptsReportResult> GetAsync(
        WarehouseApprovedReceiptsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
