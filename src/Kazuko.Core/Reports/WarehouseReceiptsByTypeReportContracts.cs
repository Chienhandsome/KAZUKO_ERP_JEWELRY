using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

/// <summary>
/// Báo cáo "Tổng hợp nhập hàng theo loại" (reports.warehouse.receipts_by_type):
/// gom các dòng phiếu nhập đã ghi sổ (STATUS_CODE = 'POSTED') theo loại vàng
/// (GOLD_TYPES.NAME, dự phòng MATERIALS.PURITY_CODE), lọc theo ngày phiếu.
/// </summary>
public sealed record WarehouseReceiptsByTypeReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record WarehouseReceiptsByTypeReportRow(
    string TypeName,
    int DocumentCount,
    int LineCount,
    decimal TotalQuantity,
    decimal TotalAmountVnd);

public sealed record WarehouseReceiptsByTypeReportResult(
    IReadOnlyList<WarehouseReceiptsByTypeReportRow> Rows,
    int TypeCount,
    int DocumentCount,
    int LineCount,
    decimal TotalQuantity,
    decimal TotalAmountVnd);

public interface IWarehouseReceiptsByTypeReportService
{
    Task<WarehouseReceiptsByTypeReportResult> GetAsync(
        WarehouseReceiptsByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
