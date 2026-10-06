using Kazuko.Core.Finance;

namespace Kazuko.Core.Sales;

public sealed record SalesLineDraft(long FinishedProductId, decimal AppliedGoldPricePerChi, decimal AppliedLaborAmountVnd, decimal AppliedStoneAmountVnd, string? OverrideReason);
/// <summary>Trusted product weight and source prices loaded by the application service.</summary>
public sealed record SalesLinePricing(long FinishedProductId, decimal GoldWeightChi, decimal SourceGoldPricePerChi, decimal SourceLaborAmountVnd, decimal SourceStoneAmountVnd);
public sealed record SalesLineAmounts(long FinishedProductId, decimal GoldAmountVnd, decimal LaborAmountVnd, decimal StoneAmountVnd, decimal TotalAmountVnd, bool IsPriceOverride);
public sealed record OldGoldLineDraft(long MaterialId, decimal ItemQuantity, decimal TotalWeightChi, decimal StoneWeightChi, decimal ImpurityWeightChi, decimal ValuePercent, decimal AppliedBuyPricePerChi, string? OverrideReason, string? Notes);
public sealed record OldGoldLineAmounts(decimal GoldWeightChi, decimal ConvertedWeightChi, decimal AmountVnd);
public sealed record SalesCheckoutDraft(Guid ClientRequestId, DateTime DocumentDate, long CustomerId, long SalespersonId, long SourceLocationId, long? OldGoldDestinationLocationId, decimal DiscountAmountVnd, IReadOnlyList<SalesLineDraft> SaleLines, IReadOnlyList<OldGoldLineDraft> OldGoldLines, IReadOnlyList<PaymentAllocationDraft> Allocations, decimal ReceivableAmountVnd, string? Notes);
public sealed record SalesCheckoutTotals(decimal SaleGrossVnd, decimal DiscountAmountVnd, decimal SaleNetVnd, decimal OldGoldTotalVnd, decimal OffsetAmountVnd, decimal NetAmountVnd, decimal ReceiptAmountVnd, decimal PayoutAmountVnd, decimal ReceivableAmountVnd);
public sealed record SalesCheckoutResult(long CheckoutId, string DocumentCode, string CheckoutType, SalesCheckoutTotals Totals, long? SalesOrderId, long? OldGoldReceiptId, bool AlreadyPosted);
public sealed record SalesCustomerOption(long Id, string Code, string Name, string? Phone, bool IsWalkIn);
public sealed record SalespersonOption(long Id, string Username, string DisplayName);
public sealed record SalesLocationOption(long Id, string Code, string Name, bool IsDefault);
public sealed record SalesProductOption(long FinishedProductId, string ProductCode, string ProductName, long RetailGoldPriceId, string RetailGoldPriceName, long SourceLocationId, string GoldPurity, long RowVersion, decimal TotalWeightChi, decimal StoneWeightChi, decimal GoldWeightChi, decimal SourceGoldPricePerChi, decimal SourceLaborAmountVnd, decimal SourceStoneAmountVnd);
public sealed record OldGoldMaterialOption(long Id, string Code, string Name, long OldGoldPriceId, decimal ValuePercent, decimal SourceBuyPricePerChi);
public sealed record SalesLineSnapshot(long FinishedProductId, string ProductCode, string ProductName, decimal GoldWeightChi, decimal SourceGoldPricePerChi, decimal SourceLaborAmountVnd, decimal SourceStoneAmountVnd, SalesLineDraft AppliedPrices, SalesLineAmounts Amounts);
public sealed record OldGoldLineSnapshot(long MaterialId, string MaterialName, decimal SourceBuyPricePerChi, OldGoldLineDraft Draft, OldGoldLineAmounts Amounts);
public sealed record SalesCheckoutDetail(long CheckoutId, string DocumentCode, string CheckoutType, DateTime DocumentDate, long CustomerId, string CustomerName, string? CustomerPhone, long SalespersonId, string SalespersonName, long CreatedBy, string CreatedByDisplayName, long? SourceLocationId, long? OldGoldDestinationLocationId, IReadOnlyList<SalesLineSnapshot> SaleLines, IReadOnlyList<OldGoldLineSnapshot> OldGoldLines, IReadOnlyList<PaymentAllocationDraft> Allocations, SalesCheckoutTotals Totals, string? Notes);
public sealed record SalesCheckoutListItem(
    long CheckoutId,
    string DocumentCode,
    string CheckoutType,
    DateTime DocumentDate,
    string CustomerName,
    string? CustomerPhone,
    string SalespersonName,
    decimal NetAmountVnd);
public sealed record SalesCheckoutSearchFilter(
    DateTime? FromDate,
    DateTime? ToDate,
    string? SearchText,
    int Limit = 100);
public sealed record SalesReceiptDto(string CompanyName, string BranchName, string? BranchAddress, string? BranchPhone, SalesCheckoutDetail Checkout);
