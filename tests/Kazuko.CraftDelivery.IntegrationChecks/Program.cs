using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Sales;
using Kazuko.Infrastructure.Production;
using Kazuko.Infrastructure.Warehouse;

// NEVER run on the application's live database. Restore a gbak backup to an
// explicitly named temporary verification database before invoking this check.
if (args.Length != 2 || !Path.GetFileName(args[0]).StartsWith("kazuko_craft_verify_", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Usage: CraftDelivery.IntegrationChecks <kazuko_craft_verify_*.fdb> <ready-order-code>");
var path = Path.GetFullPath(args[0]);
FirebirdDatabase.ApplyProfileForCurrentProcess(ConnectionProfileStore.Load(path) with { DatabasePath = path, UseEmbedded = false });
await using var db = FirebirdDatabase.CreateConnection();
await db.OpenAsync();
var orderId = await db.QuerySingleAsync<long>("SELECT ID FROM SALES_ORDERS WHERE CODE=@Code AND ORDER_TYPE='CUSTOM'", new { Code = args[1] });
var session = await db.QuerySingleAsync<AuthenticatedSession>("""
    SELECT FIRST 1 U.ID AS UserId, U.USERNAME AS Username, U.DISPLAY_NAME AS DisplayName,
           R.CODE AS RoleCode, B.ID AS BranchId, B.CODE AS BranchCode, B.NAME AS BranchName
    FROM USERS U JOIN ROLES R ON R.ID=U.ROLE_ID
    JOIN SALES_ORDERS O ON O.ID=@orderId AND O.COMPANY_ID=U.COMPANY_ID
    JOIN BRANCHES B ON B.ID=O.BRANCH_ID
    WHERE U.IS_ACTIVE=TRUE AND R.CODE='ADMIN' ORDER BY U.ID
    """, new { orderId });
var service = new FirebirdCraftOrderApplicationService(new LocalProductionAttachmentStore());
var header = await service.GetHeaderAsync(orderId, session) ?? throw new Exception("Missing header.");
Check(header.StatusCode == CraftOrderStatuses.Ready && !header.HasFinishedGoodsIssue, "ready, not already issued");
var products = await new FirebirdWarehouseRepository().SearchIssueCandidatesAsync(orderId, session);
var locationId = products.Select(x => x.LocationId).Distinct().Single() ?? throw new Exception("No location.");
Check(await db.ExecuteScalarAsync<string>("SELECT INVENTORY_SCOPE FROM LOCATIONS WHERE ID=@locationId", new { locationId }) == "FINISHED_GOODS", "products physically in finished-goods warehouse");
var fundId = await db.QuerySingleAsync<long>("SELECT FIRST 1 ID FROM CASH_FUNDS WHERE BRANCH_ID=@BranchId AND IS_ACTIVE=TRUE ORDER BY ID", session);
var draft = new CraftOrderDeliverDraft(orderId, DateTime.Now, locationId, session.DisplayName, header.CustomerName,
    "Verification database only", products.Select(x => x.Id).ToArray(),
    [new(PaymentMethods.Cash, header.RemainingDueVnd, fundId, null, null)], 0);
var before = await SnapshotAsync();
var rawLocation = await db.QuerySingleAsync<long>("SELECT FIRST 1 ID FROM LOCATIONS WHERE BRANCH_ID=@BranchId AND INVENTORY_SCOPE='RAW_MATERIAL' AND IS_ACTIVE=TRUE", session);
await RejectAsync(draft with { SourceLocationId = rawLocation }, "Kho xuất");
await RejectAsync(draft with { FinishedProductIds = draft.FinishedProductIds.Skip(1).ToArray() }, "giao đủ");
var foreignProduct = await db.QueryFirstOrDefaultAsync<long?>("""
    SELECT FIRST 1 ID FROM FINISHED_PRODUCTS
    WHERE BRANCH_ID=@BranchId AND SALES_ORDER_ID<>@orderId AND LOCATION_ID=@locationId
      AND STATUS_CODE IN ('IN_STOCK','RESERVED','DISPLAYED') ORDER BY ID
    """, new { session.BranchId, orderId, locationId });
if (foreignProduct is not null)
    await RejectAsync(draft with { FinishedProductIds = draft.FinishedProductIds.Skip(1).Prepend(foreignProduct.Value).ToArray() }, "không thuộc đơn");
// This passes validation and reaches settlement AFTER inventory posting. Its
// nonexistent fund must roll back products, movements, document and payments.
await RejectAsync(draft with { CollectionAllocations = [new(PaymentMethods.Cash, header.RemainingDueVnd, long.MaxValue, null, null)] });
Check(await SnapshotAsync() == before, "failed settlement rolled back inventory, version, vouchers, payments and order state");
var result = await service.DeliverAsync(draft, session);
var after = await service.GetHeaderAsync(orderId, session) ?? throw new Exception("Missing final header.");
Check(after.StatusCode == CraftOrderStatuses.Completed && after.HasFinishedGoodsIssue && after.RemainingDueVnd == 0, "delivery and full collection completed together");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FINISHED_PRODUCTS WHERE SALES_ORDER_ID=@orderId AND STATUS_CODE='SOLD' AND LOCATION_ID IS NULL", new { orderId }) == products.Count, "all serials sold from finished-goods warehouse");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM STOCK_DOCUMENTS WHERE SALES_ORDER_ID=@orderId AND DOCUMENT_TYPE='FINISHED_GOODS_ISSUE' AND STATUS_CODE='POSTED'", new { orderId }) == 1, "exactly one issue document");
Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM FINISHED_PRODUCT_MOVEMENTS WHERE DOCUMENT_ID=@IssueDocumentId AND DOCUMENT_TYPE='FINISHED_GOODS_ISSUE' AND MOVEMENT_TYPE='OUT'", result) == products.Count, "one outbound movement per serial");
before = await SnapshotAsync();
await RejectAsync(draft, "hoàn tất");
Console.WriteLine("CRAFT_DELIVERY_INTEGRATION_CHECKS_PASSED");

async Task RejectAsync(CraftOrderDeliverDraft rejected, string? expected = null)
{
    try { await service.DeliverAsync(rejected, session); throw new Exception("Invalid delivery was accepted."); }
    catch (Exception ex) when (ex is InvalidOperationException or FbException)
    {
        if (expected is not null && !ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) throw;
        Console.WriteLine("PASS rejected safely: " + ex.Message);
    }
    Check(await SnapshotAsync() == before, "rejection has no persisted side effects");
}

async Task<string> SnapshotAsync()
{
    var productRows = await db.QueryAsync("SELECT ID, STATUS_CODE, LOCATION_ID, ROW_VERSION FROM FINISHED_PRODUCTS WHERE SALES_ORDER_ID=@orderId ORDER BY ID", new { orderId });
    var order = await db.QuerySingleAsync("SELECT STATUS_CODE, FINISHED_GOODS_ISSUE_DOCUMENT_ID, FINISHED_GOODS_ISSUED_AT, UPDATED_AT FROM SALES_ORDERS WHERE ID=@orderId", new { orderId });
    var counts = await db.QuerySingleAsync("""
        SELECT (SELECT COUNT(*) FROM STOCK_DOCUMENTS) AS Docs,
               (SELECT COUNT(*) FROM FINISHED_PRODUCT_MOVEMENTS) AS Movements,
               (SELECT COUNT(*) FROM CASH_VOUCHERS) AS Vouchers,
               (SELECT COUNT(*) FROM FINANCIAL_SETTLEMENTS) AS Settlements,
               (SELECT COUNT(*) FROM ORDER_PAYMENTS) AS Payments,
               (SELECT COUNT(*) FROM RECEIVABLE_DEBTS) AS Debts
        FROM RDB$DATABASE
        """);
    return JsonSerializer.Serialize(new { Products = productRows, Order = order, Counts = counts });
}
static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
