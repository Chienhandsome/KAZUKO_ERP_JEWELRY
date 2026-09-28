using Kazuko.Core.Authentication;

namespace Kazuko.Core.Finance;

public static class SettlementTypes
{
    public const string Receipt = "RECEIPT";
    public const string Payment = "PAYMENT";
}

public static class SettlementStatuses
{
    public const string Posted = "POSTED";
    public const string Cancelled = "CANCELLED";
}

public static class SettlementPartyTypes
{
    public const string Supplier = "SUPPLIER";
    public const string Customer = "CUSTOMER";
    public const string ExternalWorker = "EXTERNAL_WORKER";
}

public static class SettlementApplicationTargetTypes
{
    public const string SalesOrder = "SALES_ORDER";
    public const string PayableDebt = "PAYABLE_DEBT";
    public const string ReceivableDebt = "RECEIVABLE_DEBT";
}

public sealed record SettlementApplicationDraft(
    string TargetType,
    long TargetId,
    decimal AmountVnd);

public sealed record FinancialSettlementDraft(
    string SettlementType,
    string PartyType,
    long PartyId,
    DateTime SettlementDate,
    IReadOnlyList<PaymentAllocationDraft> Allocations,
    IReadOnlyList<SettlementApplicationDraft> Applications,
    string? Notes);

public sealed record FinancialSettlementResult(
    long SettlementId,
    string Code,
    IReadOnlyList<long> CashVoucherIds,
    IReadOnlyList<long> ApplicationIds);

/// <summary>
/// One money movement (nhiều phương thức) that may apply to many debts,
/// creating exactly one CASH_VOUCHERS row per payment allocation.
/// </summary>
public interface IFinancialSettlementService
{
    Task<FinancialSettlementResult> PostAsync(
        FinancialSettlementDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
