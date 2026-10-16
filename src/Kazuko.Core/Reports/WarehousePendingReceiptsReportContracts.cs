using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

/// <summary>
/// Báo cáo "Chi tiết nhập hàng chưa xét duyệt" (reports.warehouse.pending_receipts):
/// từng dòng phiếu nhập kho còn ở trạng thái nháp (STATUS_CODE = 'DRAFT'), lọc theo ngày phiếu.
/// Schema hiện tại không có trạng thái duyệt riêng — 'DRAFT' (chưa ghi sổ) là điều kiện gần nhất với "chưa xét duyệt".
/// </summary>
public sealed record WarehousePendingReceiptsReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record WarehousePendingReceiptsReportRow(
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

public sealed record WarehousePendingReceiptsReportResult(
    IReadOnlyList<WarehousePendingReceiptsReportRow> Rows,
    int DocumentCount,
    int LineCount,
    decimal TotalQuantity,
    decimal TotalAmountVnd);

public interface IWarehousePendingReceiptsReportService
{
    Task<WarehousePendingReceiptsReportResult> GetAsync(
        WarehousePendingReceiptsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
