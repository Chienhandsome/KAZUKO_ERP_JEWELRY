using Kazuko.Core.UserManagement;

namespace Kazuko.Infrastructure.UserManagement;

public static class BackendAccess
{
    public static bool Allows(string contract, string method, object?[] args, PermissionSnapshot rights)
    {
        bool Has(string f, PermissionAction a = PermissionAction.View) => rights.Allows(f, a);
        bool Any(params string[] functions) => functions.Any(f => Has(f));
        object? Value(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);
        PermissionAction SaveAction() => Value(args.FirstOrDefault(), "Id") is long id && id > 0 ? PermissionAction.Edit : PermissionAction.Add;
        string VoucherType() => (args.FirstOrDefault(x => x is string) as string
            ?? Value(args.FirstOrDefault(), "VoucherType") as string ?? "").ToUpperInvariant();
        bool Lookup() => new[] { "SALES", "WAREHOUSE", "PRODUCTION", "DEBT", "FINANCE", "CUSTOMER", "SUPPLIER", "HR", "USER_MANAGEMENT", "PRICE_BOARD", "REPORT", "SETTINGS" }
            .Any(route => NavigationAccess.Allows(rights, route));
        if (contract.EndsWith("ReportService", StringComparison.Ordinal)) return Has("REP_VIEW");
        return contract switch
        {
            "ICustomerRepository" => method switch {
                "SaveAsync" => Has("CAT_CUSTOMERS", SaveAction()), "SetActiveAsync" => Has("CAT_CUSTOMERS", args.ElementAtOrDefault(1) is true ? PermissionAction.Edit : PermissionAction.Delete),
                "CreateGroupAsync" => Has("CAT_CUSTOMERS", PermissionAction.Add),
                "GetDebtSnapshotAsync" => Has("DEBT_CUSTOMER"), "GetPurchaseHistoryAsync" => Has("SALES_HISTORY"),
                "GetTopCustomersAsync" => Has("REP_VIEW"),
                "GetGroupsAsync" or "FindDuplicateWarningAsync" or "GetByIdAsync" or "SearchAsync" => Any("CAT_CUSTOMERS", "SALES_RETAIL", "SALES_WHOLESALE", "OLD_GOLD_PURCHASE", "CRAFT_ORDER", "DEBT_CUSTOMER", "DEBT_SETTLEMENT", "INV_MATERIAL_RECEIPT", "PROD_ORDER", "REP_VIEW"), _ => false },
            "ISupplierRepository" => method switch {
                "GetGroupDetailsAsync" or "GetGroupByIdAsync" => Has("CAT_SUPPLIERS"),
                "SaveGroupAsync" => Has("CAT_SUPPLIERS", SaveAction()),
                "DeleteGroupAsync" => Has("CAT_SUPPLIERS", PermissionAction.Delete),
                "GetPurchaseHistoryAsync" => Any("CAT_SUPPLIERS", "INV_HISTORY", "DEBT_CUSTOMER", "REP_VIEW"),
                "GetTopSuppliersAsync" => Has("REP_VIEW"),
                "SaveAsync" => Has("CAT_SUPPLIERS", SaveAction()), "SetActiveAsync" => Has("CAT_SUPPLIERS", args.ElementAtOrDefault(1) is true ? PermissionAction.Edit : PermissionAction.Delete),
                "GetGroupsAsync" or "SearchAsync" or "GetByIdAsync" or "FindDuplicateWarningAsync" => Any("CAT_SUPPLIERS", "INV_MATERIAL_RECEIPT", "INV_FINISHED_RECEIPT", "DEBT_CUSTOMER", "DEBT_SETTLEMENT", "REP_VIEW"), _ => false },
            "IHumanResourcesRepository" => method switch {
                "SaveAsync" => Has("CAT_EMPLOYEES", SaveAction()), "SetActiveAsync" => Has("CAT_EMPLOYEES", args.ElementAtOrDefault(1) is true ? PermissionAction.Edit : PermissionAction.Delete),
                "SearchAsync" or "GetByIdAsync" => Has("CAT_EMPLOYEES"),
                "GetDepartmentsAsync" or "GetPositionsAsync" or "IsAttendanceCodeTakenAsync" or "SuggestNextAttendanceCodeAsync" => Has("CAT_EMPLOYEES"), _ => false },
            "IExternalWorkerRepository" => method == "SaveAsync" ? Has("CAT_EXTERNAL_WORKERS", SaveAction()) : Any("CAT_EXTERNAL_WORKERS", "PROD_ORDER", "PROD_MATERIAL_ISSUE", "PROD_GOLD_RECONCILE", "INV_FINISHED_RECEIPT", "DEBT_WORKER_GOLD"),
            "IWarehouseGoldPriceRepository" => Has("CAT_GOLD_PRICE", method == "SaveAsync" ? PermissionAction.Edit : PermissionAction.View),
            "IGoldTypeCatalogService" => method switch {
                "CreateAsync" => Has("CAT_GOLD_PRICE", PermissionAction.Add), "UpdateAsync" => Has("CAT_GOLD_PRICE", PermissionAction.Edit),
                "DeactivateAsync" => Has("CAT_GOLD_PRICE", PermissionAction.Delete), "ListAsync" or "GetByIdAsync" => Has("CAT_GOLD_PRICE") || NavigationAccess.Allows(rights, "WAREHOUSE") || NavigationAccess.Allows(rights, "SALES") || NavigationAccess.Allows(rights, "PRODUCTION") || Any("DEBT_WORKER_GOLD", "REP_VIEW"), _ => false },
            "IPriceBoardRepository" => Has("CAT_GOLD_PRICE", method switch {
                "SaveGoldPricesAsync" or "SaveCurrencyRatesAsync" => PermissionAction.Edit,
                "AddCustomGoldTypeAsync" or "AddCurrencyRateAsync" => PermissionAction.Add,
                "DeleteGoldPriceAsync" or "DeleteCurrencyRateAsync" => PermissionAction.Delete, _ => PermissionAction.View }),
            "IMaterialReceiptService" => Has("INV_MATERIAL_RECEIPT", PermissionAction.Add),
            "IMaterialIssueService" or "IStockTransferService" => Has("INV_TRANSFER", PermissionAction.Add),
            "IStockAdjustmentService" => Has("INV_STOCKTAKE", PermissionAction.Add),
            "IFinishedGoodsIssueService" => method == "ReverseAsync" ? Has("INV_REVERSE", PermissionAction.Delete) : Has("INV_TRANSFER", PermissionAction.Add),
            "IOutsourcedCraftService" => method == "SearchHoldingsAsync" ? Has("PROD_MATERIAL_ISSUE") : Has("PROD_MATERIAL_ISSUE", PermissionAction.Add),
            "IFinishedProductTransferService" => method switch {
                "CreateRequestAsync" => Has("INV_TRANSFER", PermissionAction.Add),
                "ApproveAsync" or "RejectAsync" => Has("INV_SALES_INTAKE", PermissionAction.Edit),
                "ListRequestsAsync" or "GetRequestDetailAsync" => Any("INV_SALES_INTAKE", "INV_TRANSFER"), _ => false },
            "IWarehouseRepository" => method switch {
                "CreateFinishedProductGroupAsync" => Has("INV_FINISHED_RECEIPT", PermissionAction.Add),
                "GetLocationsAsync" or "GetFinishedProductGroupsAsync" or "SearchMaterialsAsync" or "SearchIssueSalesOrdersAsync" or "SearchIssueCandidatesAsync" => NavigationAccess.Allows(rights, "WAREHOUSE") || NavigationAccess.Allows(rights, "SALES") || NavigationAccess.Allows(rights, "PRODUCTION"),
                "SearchMaterialBalancesAsync" or "SearchFinishedProductStockAsync" or "SearchSaleItemStockAsync" => Has("INV_STOCK_VIEW"),
                "SearchDocumentsAsync" or "GetDocumentAsync" or "GetDocumentLinesAsync" or "SearchMovementsAsync" => Has("INV_HISTORY"), _ => false },
            "IProductionApplicationService" => method switch {
                "SaveDraftAsync" => Has("PROD_ORDER", SaveAction()),
                "PostMaterialIssueAsync" => Has("PROD_MATERIAL_ISSUE", PermissionAction.Add),
                "PostWorkerReturnAsync" or "RecordQcAsync" or "RecordReworkReturnAsync" => Has("PROD_QC", PermissionAction.Edit),
                "CompleteAsync" => Has("PROD_COMPLETE", PermissionAction.Edit),
                "PostFinishedReceiptAsync" or "PostManualFinishedReceiptAsync" => Has("INV_FINISHED_RECEIPT", PermissionAction.Add),
                "RecordWorkerGoldDebtAsync" => Has("PROD_GOLD_RECONCILE", PermissionAction.Add),
                "CancelAsync" => Has("PROD_CANCEL", PermissionAction.Delete), _ => false },
            "IProductionQueryRepository" => method switch {
                "GetCompletedUnreceivedOrdersAsync" or "GetExistingFinishedProductCodesAsync" => Has("INV_FINISHED_RECEIPT"),
                "SearchWorkingEmployeesAsync" or "SearchActiveExternalWorkersAsync" or "GetLocationsAsync" or "SearchEligibleSalesOrdersAsync" => NavigationAccess.Allows(rights, "PRODUCTION") || Has("INV_FINISHED_RECEIPT"),
                "GetOrderDetailAsync" => Any("PROD_ORDER", "INV_FINISHED_RECEIPT", "PROD_GOLD_RECONCILE", "PROD_QC", "PROD_COMPLETE", "PROD_MATERIAL_ISSUE"),
                "GetSummaryAsync" or "SearchOrdersAsync" => NavigationAccess.Allows(rights, "PRODUCTION"),
                "GetIssuedGoldWeightAsync" or "GetIssuedGoldByPurityAsync" or "GetIssuedStoneByMaterialAsync" => Any("PROD_GOLD_RECONCILE", "INV_FINISHED_RECEIPT", "PROD_QC"),
                "SearchMaterialStockAsync" or "GetMaterialBalanceAllocationsAsync" or "GetQcUnitComponentsAsync" => Any("PROD_ORDER", "PROD_MATERIAL_ISSUE", "PROD_QC"), _ => false },
            "IProductionPrintDataService" => method switch {
                "GetOrderPrintAsync" => Has("PROD_ORDER"), "GetMaterialIssuePrintAsync" => Has("PROD_MATERIAL_ISSUE"),
                "GetReturnPrintAsync" or "GetQcPrintAsync" => Has("PROD_QC"), "GetFinishedReceiptPrintAsync" => Has("INV_FINISHED_RECEIPT"),
                "GetGoldSettlementPrintAsync" => Has("PROD_GOLD_RECONCILE"), _ => false },
            "ISalesCheckoutApplicationService" => args.FirstOrDefault() is Kazuko.Core.Sales.SalesCheckoutDraft sale
                && (sale.SaleLines.Count == 0 || Has("SALES_RETAIL", PermissionAction.Add))
                && (sale.OldGoldLines.Count == 0 || Has("OLD_GOLD_PURCHASE", PermissionAction.Add)),
            "IWholesaleSalesApplicationService" => Has("SALES_WHOLESALE", method == "PostAsync" ? PermissionAction.Add : PermissionAction.View),
            "ISalesQueryRepository" => method switch {
                "SearchCustomersAsync" or "GetSalespeopleAsync" => NavigationAccess.Allows(rights, "SALES") || Has("PROD_ORDER"),
                "GetCheckoutAsync" or "SearchPostedCheckoutsAsync" or "GetAdjacentCheckoutIdAsync" => Has("SALES_HISTORY"),
                "GetOldGoldMaterialsAsync" or "GetOldGoldLocationsAsync" => Any("OLD_GOLD_PURCHASE", "SALES_RETAIL"),
                "FindAvailableProductAsync" or "SearchAvailableProductsAsync" or "CountMatchingFinishedProductsAsync" => Has("SALES_RETAIL"), _ => false },
            "ISalesPrintDataService" => Any("SALES_HISTORY", "SALES_RETAIL", "SALES_WHOLESALE", "OLD_GOLD_PURCHASE"),
            "ISalesAuthorizationService" => NavigationAccess.Allows(rights, "SALES"),
            "ICraftOrderApplicationService" => method is "GetHeaderAsync" or "GetLinesAsync" ? Any("CRAFT_ORDER", "PROD_ORDER") : Has("CRAFT_ORDER", method switch {
                "CreateWithDepositAsync" => PermissionAction.Add, "CollectRemainingAsync" or "DeliverAsync" => PermissionAction.Edit, _ => PermissionAction.View }),
            "IPayableDebtRepository" => method == "PayAsync" ? Has("DEBT_SETTLEMENT", PermissionAction.Add) : Has("DEBT_CUSTOMER"),
            "IReceivableDebtRepository" or "IPartyCreditService" or "IFinancialSettlementService" => Has("DEBT_SETTLEMENT", PermissionAction.Add),
            "IDebtWorkbenchRepository" => method switch {
                "RecordWorkerGoldRepaymentAsync" => Has("DEBT_WORKER_GOLD", PermissionAction.Edit),
                "SearchWorkerGoldDebtsAsync" or "GetWorkerGoldDebtPaymentsAsync" => Has("DEBT_WORKER_GOLD"),
                "PayCustomerFifoAsync" or "CollectSupplierPaymentAsync" or "CollectCustomerPaymentAsync" or "PaySupplierFifoAsync" or "PayPayablePartyFifoAsync" or "SettleSingleLedgerEntryAsync" or "SettleDebtAllocationsAsync" => Has("DEBT_SETTLEMENT", PermissionAction.Add),
                "GetSummaryAsync" => Has("DEBT_CUSTOMER"), _ => Has("DEBT_CUSTOMER") },
            "IDocumentReversalService" => Has(method == "ReverseStockDocumentAsync" ? "INV_REVERSE" : "FIN_REVERSE", PermissionAction.Delete),
            "IFinancialVoucherPostingService" => Has(method == "PostReceiptAsync" ? "FIN_CASH_IN" : "FIN_CASH_OUT", PermissionAction.Add),
            "IFinanceVoucherService" => method == "CancelVoucherAsync" ? Has("FIN_REVERSE", PermissionAction.Delete)
                : Has(VoucherType() == "IN" ? "FIN_CASH_IN" : "FIN_CASH_OUT", PermissionAction.Add),
            "IFinanceDashboardRepository" => method == "SearchVouchersAsync" ? (VoucherType() == "" ? Has("FIN_CASH_IN") && Has("FIN_CASH_OUT") : Has(VoucherType() == "IN" ? "FIN_CASH_IN" : "FIN_CASH_OUT"))
                : method == "GetVoucherDetailAsync" ? Any("FIN_CASH_IN", "FIN_CASH_OUT") : Has("FIN_FUND"),
            "ICashFundRepository" or "IBankAccountRepository" => NavigationAccess.Allows(rights, "SALES") || NavigationAccess.Allows(rights, "FINANCE") || Any("DEBT_SETTLEMENT", "INV_MATERIAL_RECEIPT", "PROD_ORDER"),
            "IUserManagementRepository" => method switch {
                "SaveUserAsync" or "SaveGroupAsync" => Has("SYS_USER_MANAGEMENT", SaveAction()),
                "DeleteUserAsync" or "DeleteGroupAsync" => Has("SYS_USER_MANAGEMENT", PermissionAction.Delete),
                "SaveGroupPermissionsAsync" => Has("SYS_USER_MANAGEMENT", PermissionAction.Edit), _ => Has("SYS_USER_MANAGEMENT") },
            "ICompanyProfileService" => method == "GetAsync" ? Lookup() : Has("SYS_SETTINGS", PermissionAction.Edit),
            "IPrintTemplateStore" => method == "SaveAsync" ? Has("SYS_SETTINGS", PermissionAction.Edit) : Lookup(),
            "IDatabaseManagementService" => method == "CreateBlankDatabaseAsync" ? Has("SYS_DATABASE", PermissionAction.Add) && Has("SYS_DATA_DELETE", PermissionAction.Delete)
                : Has("SYS_DATABASE", method == "GetInfoAsync" || method == "ValidateDatabaseFileAsync" ? PermissionAction.View : PermissionAction.Edit),
            _ => false
        };
    }
}
