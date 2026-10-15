namespace Kazuko.Core.Sales;

public static class SalesPermissions
{
    public const string CheckoutPost = "sales.checkout.post";
    public const string WholesalePost = "sales.wholesale.post";
    public const string PriceOverride = "sales.price.override";
    public const string DebtApprove = "sales.debt.approve";
    public const string SalespersonChange = "sales.salesperson.change";
    public const string HistoryRead = "sales.history.read";
    public const string ReceiptPrint = "sales.receipt.print";
}

public static class SalesCheckoutTypes
{
    public const string Sale = "SALE";
    public const string TradeIn = "TRADE_IN";
    public const string OldGoldPurchase = "OLD_GOLD_PURCHASE";
}

public static class SalesCheckoutStatuses
{
    public const string Posted = "POSTED";
}

public sealed record SalesPermissionSnapshot(
    bool CanPost,
    bool CanOverridePrice,
    bool CanApproveDebt,
    bool CanChangeSalesperson,
    bool CanReadHistory,
    bool CanPrintReceipt,
    bool CanPostWholesale = false);
/// <summary>HasPriceOverride is resolved against trusted source prices by the application service.</summary>
public sealed record SalesValidationContext(bool IsWalkInCustomer, long CurrentUserId, SalesPermissionSnapshot Permissions, bool HasPriceOverride);
