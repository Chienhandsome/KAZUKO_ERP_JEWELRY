using System.IO;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Kazuko.Core.Production;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.Sales;
using Kazuko.Infrastructure.Warehouse;
using Kazuko.Infrastructure.Production;
using Kazuko.Modules.Finance;

if (args.Length < 1) throw new ArgumentException("Explicit isolated database required.");
var path = Path.GetFullPath(args[0]);
var root = Path.GetFullPath(@"D:\Working\artifacts\money-flow-gd3");
if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase)
    || !Path.GetFileName(path).StartsWith("test91_moneyflow_", StringComparison.OrdinalIgnoreCase)
    || !path.EndsWith(".fdb", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)
    || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
    || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
    throw new ArgumentException("Only an existing isolated test91_moneyflow_*.fdb in money-flow-gd3 is allowed.");
FirebirdDatabase.ApplyProfileForCurrentProcess(ConnectionProfileStore.Load(path) with
    { DatabasePath = path, Host = "localhost", Port = 3050, UseEmbedded = false });
await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync();
var schema = await c.ExecuteScalarAsync<int>("SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS");
if (schema != 91) throw new InvalidOperationException("Expected schema 91, found " + schema);
if (args.Contains("--probe"))
{
    Console.WriteLine("SCHEMA=" + schema);
    foreach (var table in new[] { "USERS", "BRANCHES", "CUSTOMERS", "CASH_FUNDS", "BANK_ACCOUNTS", "LOCATIONS", "MATERIALS", "FINISHED_PRODUCTS" })
        Console.WriteLine(table + "=" + await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM " + table));
    Console.WriteLine(JsonSerializer.Serialize(await c.QueryAsync("SELECT ID,BRANCH_ID,INVENTORY_SCOPE FROM LOCATIONS WHERE IS_ACTIVE=TRUE")));
    Console.WriteLine(JsonSerializer.Serialize(await c.QueryAsync("SELECT ID,BRANCH_ID,STATUS_CODE,LOCATION_ID FROM FINISHED_PRODUCTS WHERE STATUS_CODE IN ('IN_STOCK','DISPLAYED')")));
    Console.WriteLine(JsonSerializer.Serialize(await c.QueryAsync("SELECT TRIM(RDB$FIELD_NAME) AS FIELD_NAME FROM RDB$RELATION_FIELDS WHERE RDB$RELATION_NAME='SALES_ORDERS' ORDER BY RDB$FIELD_POSITION")));
    return;
}
var results = new List<object>();
var tag = "G3_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
var branch = await c.QueryFirstAsync<long>("SELECT B.ID FROM BRANCHES B JOIN COMPANIES C ON C.ID=B.COMPANY_ID WHERE C.CODE='KAZUKO' AND B.IS_ACTIVE=TRUE ORDER BY B.ID");
var company = await c.ExecuteScalarAsync<long>("SELECT COMPANY_ID FROM BRANCHES WHERE ID=@branch", new { branch });
var user = await c.QueryFirstAsync<long>("SELECT U.ID FROM USERS U JOIN ROLES R ON R.ID=U.ROLE_ID WHERE R.CODE='ADMIN' AND U.IS_ACTIVE=TRUE ORDER BY U.ID");
var session = new AuthenticatedSession(user, "gd3", "GD3 isolated test", "ADMIN", branch, "TEST", "Test");
var fund = await c.QueryFirstAsync<long>("SELECT ID FROM CASH_FUNDS WHERE BRANCH_ID=@branch AND IS_ACTIVE=TRUE ORDER BY IS_DEFAULT DESC,ID", new { branch });
var bank = await c.QueryFirstAsync<long>("SELECT ID FROM BANK_ACCOUNTS WHERE BRANCH_ID=@branch AND IS_ACTIVE=TRUE ORDER BY ID", new { branch });
var bank2 = await c.QuerySingleAsync<long>("INSERT INTO BANK_ACCOUNTS(COMPANY_ID,BRANCH_ID,CODE,BANK_NAME,ACCOUNT_NUMBER,IS_ACTIVE) VALUES(@company,@branch,@tag,'GD3 bank',@tag,TRUE) RETURNING ID", new { company, branch, tag });
var workbench = new FirebirdDebtWorkbenchRepository();
var dashboard = new FirebirdFinanceDashboardRepository();
var reversals = new FirebirdDocumentReversalService();
var vouchers = new FirebirdFinanceVoucherService();
var sales = new FirebirdSalesCheckoutApplicationService();
var queries = new FirebirdSalesQueryRepository();
var raw = await c.QueryFirstAsync<long>("SELECT ID FROM LOCATIONS WHERE BRANCH_ID=@branch AND INVENTORY_SCOPE='RAW_MATERIAL' AND IS_ACTIVE=TRUE", new { branch });
var floor = await c.QueryFirstAsync<long>("SELECT ID FROM LOCATIONS WHERE BRANCH_ID=@branch AND INVENTORY_SCOPE='SALES_FLOOR' AND IS_ACTIVE=TRUE", new { branch });
var template = await c.QueryFirstAsync<long>("SELECT ID FROM FINISHED_PRODUCTS WHERE BRANCH_ID=@branch AND LOCATION_ID=@floor AND RETAIL_GOLD_PRICE_ID IS NOT NULL ORDER BY ID", new { branch, floor });

await Case("T01/MF-01", async () =>
{
    var customer = await Customer(); var order = await Order(customer, 10_000_000);
    var debt=await Receivable(customer,order,10_000_000);
    var before = await dashboard.GetKpiAsync(session);
    await workbench.SettleDebtAllocationsAsync(new([new("RECEIVABLE_DEBT",debt,10_000_000)],4_000_000,6_000_000,0,fund,bank,tag),session);
    var after = await dashboard.GetKpiAsync(session);
    var sid = await c.ExecuteScalarAsync<long>("SELECT MAX(SETTLEMENT_ID) FROM FINANCIAL_SETTLEMENT_APPLICATIONS WHERE TARGET_TYPE='RECEIVABLE_DEBT' AND TARGET_ID=@debt",new{debt});
    var allocated = await c.ExecuteScalarAsync<decimal>("SELECT SUM(AMOUNT_VND) FROM FINANCIAL_SETTLEMENT_ALLOCATIONS WHERE SETTLEMENT_ID=@sid",new{sid});
    var delta = after.CashFundBalance-before.CashFundBalance+after.BankBalanceTotal-before.BankBalanceTotal;
    Require(allocated==10_000_000 && delta==0,"bulk discrepancy not reproduced");
    return new { order, debt, sid, allocated, dashboardBalanceDelta=delta, voucherTotal=await VoucherTotal(sid) };
});
await Case("T01/MF-11-mixed-order",async()=>
{
    var order=await Order(await Customer(),10000);var before=await Snapshot();
    var error=await Reject(()=>workbench.SettleDebtAllocationsAsync(new([new("SALES_ORDER",order,10000)],4000,6000,0,fund,bank,tag),session));
    Require(error.Contains("CK_ORDER_PAYMENT_METHOD") && before==await Snapshot(),"mixed order constraint/rollback not reproduced");
    return new{order,error,rollback=true};
});
await Case("T02/MF-01", async () =>
{
    var customer=await Customer(); var order=await Order(customer,10_000_000);
    await workbench.SettleSingleLedgerEntryAsync(new("SALES_ORDER",order,[new("BANK_TRANSFER",4_000_000,null,bank,null),new("BANK_TRANSFER",6_000_000,null,bank2,null)],tag),session);
    var sid=await LastSettlement(order);
    var header=await c.QuerySingleAsync("SELECT V.AMOUNT_VND,V.BANK_ACCOUNT_ID FROM CASH_VOUCHERS V JOIN FINANCIAL_SETTLEMENT_ALLOCATIONS A ON A.CASH_VOUCHER_ID=V.ID WHERE A.SETTLEMENT_ID=@sid GROUP BY V.AMOUNT_VND,V.BANK_ACCOUNT_ID",new{sid});
    Require((long)header.BANK_ACCOUNT_ID==bank && (decimal)header.AMOUNT_VND==10_000_000,"first-bank assignment not reproduced");
    return new{order,sid,header,allocationToSecondBank=6_000_000};
});
await Case("T03/MF-09", async () =>
{
    ManualVoucherRequest? sent=null;
    var accessor=new CurrentSessionAccessor();accessor.Set(session);
    Sta(()=>
    {
        var fake=new CaptureVouchers(r=>sent=r);
        var vm=new CashVoucherDialogViewModel(fake,accessor,[],[new(bank,"A","GD3 A","A"),new(bank2,"B","GD3 B","B")],"IN");
        vm.SelectedBank=vm.Banks.Single(x=>x.Id==bank2);vm.BankTransferAmountText="1.000";vm.SaveCommand.Execute(null);
        Require(sent is not null,"UI did not send manual request: "+vm.ErrorMessage);
    });
    var voucher=await vouchers.CreateManualVoucherAsync(sent!,session);
    var actual=await c.ExecuteScalarAsync<long>("SELECT BANK_ACCOUNT_ID FROM CASH_VOUCHERS WHERE ID=@Id",voucher);
    Require(actual!=bank2,"selected bank was unexpectedly retained");
    var error=await Reject(()=>vouchers.CreateManualVoucherAsync(sent! with{PaymentMethod="MIXED"},session));
    return new{voucherId=voucher.Id,uiSelectedBank=bank2,actualBank=actual,mixedError=error};
});
await Case("T04/MF-06", async () =>
{
    var before=await Snapshot();
    var error=await Reject(()=>vouchers.CreateManualVoucherAsync(new("IN",DateTime.Today,"CARD",1000,tag,tag,null),session));
    Require(before==await Snapshot(),"CARD failure had persistent side effects");
    var constraint=await c.QueryAsync<string>("SELECT TRIM(T.RDB$TRIGGER_SOURCE) FROM RDB$CHECK_CONSTRAINTS C JOIN RDB$TRIGGERS T ON T.RDB$TRIGGER_NAME=C.RDB$TRIGGER_NAME WHERE C.RDB$CONSTRAINT_NAME='CK_CV_METHOD'");
    return new{error,constraint,rollback=true};
});
await Case("T05/MF-02", async () =>
{
    var customer=await Customer();var order=await Order(customer,5000);
    await workbench.SettleDebtAllocationsAsync(new([new("SALES_ORDER",order,5000)],5000,0,0,fund,null,tag),session);
    var sid=await LastSettlement(order);var vid=await c.ExecuteScalarAsync<long>("SELECT FIRST 1 CASH_VOUCHER_ID FROM FINANCIAL_SETTLEMENT_ALLOCATIONS WHERE SETTLEMENT_ID=@sid",new{sid});
    await vouchers.CancelVoucherAsync(vid,tag,session);
    var payment=await c.ExecuteScalarAsync<decimal>("SELECT SUM(AMOUNT) FROM ORDER_PAYMENTS WHERE ORDER_ID=@order AND IS_VOIDED=FALSE",new{order});
    var status=await c.ExecuteScalarAsync<string>("SELECT STATUS_CODE FROM FINANCIAL_SETTLEMENTS WHERE ID=@sid",new{sid});
    Require(payment==5000 && status=="POSTED" && await VoucherTotal(sid)==0,"owner divergence not reproduced");
    return new{order,sid,vid,payment,settlementStatus=status};
});
foreach(var reverseOrder in new[]{false,true}) await Case("T06/MF-03/"+(reverseOrder?"receivable-first":"order-first"),async()=>
{
    var customer=await Customer();var draft=await Sale(customer,10_000_000,4_000_000,6_000_000);
    var posted=await sales.PostAsync(draft,session);var order=posted.SalesOrderId!.Value;
    var debt=await c.ExecuteScalarAsync<long>("SELECT RECEIVABLE_DEBT_ID FROM SALES_CHECKOUTS WHERE ID=@CheckoutId",posted);
    var row=(await workbench.SearchCustomerDebtsAsync(new(tag,0,"",null,null),session)).Single(x=>x.CustomerId==customer);
    Require(row.ReceivableRemainingVnd==12_000_000,"duplicate balance not reproduced");
    async Task Collect(string type,long id)=>await workbench.SettleDebtAllocationsAsync(new([new(type,id,6_000_000)],6_000_000,0,0,fund,null,tag),session);
    if(reverseOrder){await Collect("RECEIVABLE_DEBT",debt);await Collect("SALES_ORDER",order);}else{await Collect("SALES_ORDER",order);await Collect("RECEIVABLE_DEBT",debt);}
    var sids=(await c.QueryAsync<long>("SELECT S.ID FROM FINANCIAL_SETTLEMENTS S WHERE S.PARTY_ID=@customer AND S.PARTY_TYPE='CUSTOMER'",new{customer})).ToArray();
    decimal total=0;foreach(var sid in sids)total+=await VoucherTotal(sid);
    Require(total==16_000_000,"two independent collections not reproduced");
    foreach(var sid in sids)await reversals.ReverseSettlementAsync(sid,tag,session);
    return new{order,debt,initialDisplayReceivable=row.ReceivableRemainingVnd,totalCashRecorded=total,orderValue=10_000_000,allSettlementReversalsCompleted=true};
});
await Case("T07/MF-04",async()=>
{
    var customer=await Customer();var draft=await Sale(customer,10_000_000,3_000_000,0);
    var oldGold=(await queries.GetOldGoldMaterialsAsync(session)).First();
    var oldLocation=(await queries.GetOldGoldLocationsAsync(session)).First();
    draft=draft with{OldGoldDestinationLocationId=oldLocation.Id,OldGoldLines=[new(oldGold.Id,1,1,0,0,100,7_000_000,"GD3 price",tag)]};
    var posted=await sales.PostAsync(draft,session);
    var open=(await workbench.GetCustomerOpenReceivablesAsync(customer,session)).Single();
    Require(posted.Totals.ReceivableAmountVnd==0 && open.RemainingAmountVnd==7_000_000,"offset omission not reproduced");
    await workbench.SettleDebtAllocationsAsync(new([new("SALES_ORDER",open.OrderId,7_000_000)],7_000_000,0,0,fund,null,tag),session);
    return new{posted.CheckoutId,open.OrderId,actualDebt=0,offeredAndCollected=7_000_000};
});
foreach(var reference in new string?[]{null,"","BANK-GD3"})await Case("T08/MF-05/"+(reference??"null"),async()=>
{
    var customer=await Customer();var draft=await Sale(customer,3000,3000,0);
    draft=draft with{Allocations=[new("BANK_TRANSFER",3000,null,bank,reference)]};
    var posted=await sales.PostAsync(draft,session);var order=posted.SalesOrderId!.Value;
    var sid=await c.ExecuteScalarAsync<long>("SELECT FINANCIAL_SETTLEMENT_ID FROM SALES_CHECKOUTS WHERE ID=@CheckoutId",posted);
    await reversals.ReverseSettlementAsync(sid,tag,session);
    var remaining=await c.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(AMOUNT),0) FROM ORDER_PAYMENTS WHERE ORDER_ID=@order AND IS_VOIDED=FALSE",new{order});
    Require(remaining==(reference is null?0:3000),"unexpected reference behavior");
    return new{order,sid,reference,stillEffectivePayment=remaining,voucherTotal=await VoucherTotal(sid)};
});
await Case("T09/MF-07",async()=>
{
    var other=await c.QuerySingleAsync<long>("INSERT INTO BRANCHES(COMPANY_ID,CODE,NAME,IS_ACTIVE) VALUES(@company,@tag,'GD3 other branch',TRUE) RETURNING ID",new{company,tag});
    var customer=await Customer();var order=await Order(customer,3000,other);
    await workbench.SettleDebtAllocationsAsync(new([new("SALES_ORDER",order,3000)],3000,0,0,fund,null,tag),session);
    var sid=await LastSettlement(order);var recordedBranch=await c.ExecuteScalarAsync<long>("SELECT BRANCH_ID FROM FINANCIAL_SETTLEMENTS WHERE ID=@sid",new{sid});
    Require(recordedBranch==branch && other!=branch,"cross-branch posting not reproduced");
    return new{order,sourceBranch=other,postedBranch=recordedBranch,sid};
});
await Case("T10/MF-10",async()=>
{
    var supplier=await c.QueryFirstAsync<long>("SELECT ID FROM SUPPLIERS WHERE COMPANY_ID=@company AND IS_ACTIVE=TRUE",new{company});
    var material=await c.QueryFirstAsync<long>("SELECT ID FROM MATERIALS WHERE COMPANY_ID=@company AND IS_ACTIVE=TRUE",new{company});
    var posted=await new FirebirdMaterialReceiptService().CreateDraftAndPostAsync(new(DateTime.Today,raw,"SUPPLIER",supplier,null,tag,[new(material,1,10000,"VND",1,tag)],0,[]),session);
    var debt=await c.ExecuteScalarAsync<long>("SELECT ID FROM PAYABLE_DEBTS WHERE ORIGIN_DOCUMENT_TYPE='STOCK_DOCUMENT' AND ORIGIN_DOCUMENT_ID=@DocumentId",posted);
    await workbench.SettleDebtAllocationsAsync(new([new("PAYABLE_DEBT",debt,10000)],10000,0,0,fund,null,tag),session);
    var sid=await c.ExecuteScalarAsync<long>("SELECT MAX(SETTLEMENT_ID) FROM FINANCIAL_SETTLEMENT_APPLICATIONS WHERE TARGET_TYPE='PAYABLE_DEBT' AND TARGET_ID=@debt",new{debt});
    await reversals.ReverseSettlementAsync(sid,tag,session);
    var error=await Reject(()=>reversals.ReverseStockDocumentAsync(posted.DocumentId,tag,session));
    Require(error.Contains("thanh toán",StringComparison.OrdinalIgnoreCase),"did not reach payment dependent guard");
    return new{posted.DocumentId,debt,sid,error};
});
await Case("T11/MF-08-and-rollback",async()=>
{
    var manual=await vouchers.CreateManualVoucherAsync(new("OUT",DateTime.Today,"CASH",1000,tag,tag,null),session);
    var before=await Snapshot();
    await c.ExecuteAsync("CREATE EXCEPTION E_GD3_AUDIT 'GD3 audit fault'");
    await c.ExecuteAsync("CREATE TRIGGER T_GD3_AUDIT FOR AUDIT_LOG ACTIVE BEFORE INSERT POSITION 0 AS BEGIN EXCEPTION E_GD3_AUDIT; END");
    string fault;
    try
    {
        fault=await Reject(()=>vouchers.CreateManualVoucherAsync(new("IN",DateTime.Today,"CASH",2000,tag,tag,null),session));
        Require(before==await Snapshot(),"manual posting did not rollback");
        await vouchers.CancelVoucherAsync(manual.Id,tag,session);
        Require(await c.ExecuteScalarAsync<string>("SELECT STATUS_CODE FROM CASH_VOUCHERS WHERE ID=@Id",manual)=="CANCELLED","cancel did not bypass failed audit");
    }
    finally{await c.ExecuteAsync("DROP TRIGGER T_GD3_AUDIT");await c.ExecuteAsync("DROP EXCEPTION E_GD3_AUDIT");}
    return new{manual.Id,fault,postingRollback=true,cancellationSucceededWithoutAudit=true};
});
await Case("T12/checkout-retry-and-race",async()=>
{
    var customer=await Customer();var draft=await Sale(customer,1000,1000,0);
    var first=await sales.PostAsync(draft,session);var replay=await sales.PostAsync(draft,session);
    Require(replay.AlreadyPosted && first.CheckoutId==replay.CheckoutId,"retry duplicated checkout");
    var changedError=await Reject(()=>sales.PostAsync(draft with{Notes=tag+"changed"},session));
    var racing=await Sale(customer,2000,2000,0);var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<string> Post(SalesCheckoutDraft d){await gate.Task;try{await sales.PostAsync(d,session);return "success";}catch(Exception ex)when(ex is FbException or InvalidOperationException){return ex.GetType().Name+": "+ex.Message;}}
    var a=Task.Run(()=>Post(racing));var b=Task.Run(()=>Post(racing with{ClientRequestId=Guid.NewGuid()}));gate.SetResult();
    var attempts=await Task.WhenAll(a,b);Require(attempts.Count(x=>x=="success")==1,"race did not have exactly one winner");
    return new{first.CheckoutId,replay.AlreadyPosted,changedError,attempts};
});
await Case("T12/debt-retry-and-race",async()=>
{
    var customer=await Customer();var order=await Order(customer,10000);
    var request=new DebtAllocationSettleRequest([new("SALES_ORDER",order,2000)],2000,0,0,fund,null,tag);
    await workbench.SettleDebtAllocationsAsync(request,session);await workbench.SettleDebtAllocationsAsync(request,session);
    var repeated=await c.ExecuteScalarAsync<decimal>("SELECT SUM(AMOUNT) FROM ORDER_PAYMENTS WHERE ORDER_ID=@order AND IS_VOIDED=FALSE",new{order});
    Require(repeated==4000,"retry did not demonstrate missing request dedupe");
    var raceOrder=await Order(customer,5000);var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task<string> Pay(){await gate.Task;try{await workbench.SettleDebtAllocationsAsync(new([new("SALES_ORDER",raceOrder,5000)],5000,0,0,fund,null,tag),session);return "success";}catch(Exception ex)when(ex is FbException or InvalidOperationException){return ex.GetType().Name+": "+ex.Message;}}
    var a=Task.Run(Pay);var b=Task.Run(Pay);gate.SetResult();var attempts=await Task.WhenAll(a,b);
    Require(attempts.Count(x=>x=="success")==1,"debt race did not have one winner");
    return new{order,repeatedRequestAmount=2000,recordedAmount=repeated,raceOrder,attempts};
});
await Case("T12/checkout-audit-rollback",async()=>
{
    var draft=await Sale(await Customer(),9000,9000,0);var before=await Snapshot();
    await c.ExecuteAsync("CREATE EXCEPTION E_GD3_AUDIT 'GD3 audit fault'");
    await c.ExecuteAsync("CREATE TRIGGER T_GD3_AUDIT FOR AUDIT_LOG ACTIVE BEFORE INSERT POSITION 0 AS BEGIN EXCEPTION E_GD3_AUDIT; END");
    string error;
    try{error=await Reject(()=>sales.PostAsync(draft,session));Require(before==await Snapshot(),"checkout audit fault left data");}
    finally{await c.ExecuteAsync("DROP TRIGGER T_GD3_AUDIT");await c.ExecuteAsync("DROP EXCEPTION E_GD3_AUDIT");}
    return new{error,rollback=true};
});
foreach(var receivedWeight in new[]{2.5m,3m,1m})await Case("T14/gold/"+receivedWeight,async()=>
{
    var production=new FirebirdProductionApplicationService(new LocalProductionAttachmentStore());
    var employee=await c.QueryFirstAsync<long>("SELECT ID FROM HR_EMPLOYEES WHERE IS_ACTIVE=TRUE ORDER BY ID");
    var finished=await c.QueryFirstAsync<long>("SELECT ID FROM LOCATIONS WHERE BRANCH_ID=@branch AND INVENTORY_SCOPE='FINISHED_GOODS' AND IS_ACTIVE=TRUE",new{branch});
    var gold=await c.QueryFirstAsync<(long Id,string Code,string Name,long MaterialId)>("SELECT G.ID,G.CODE,G.NAME,M.ID FROM GOLD_TYPES G JOIN MATERIALS M ON M.GOLD_TYPE_ID=G.ID JOIN STOCK_BALANCES B ON B.MATERIAL_ID=M.ID JOIN WAREHOUSE_GOLD_PRICES P ON P.GOLD_TYPE_ID=G.ID WHERE G.IS_ACTIVE=TRUE AND B.LOCATION_ID=@raw AND B.QUANTITY>3 AND P.ISSUE_PRICE_PER_CHI>0 ORDER BY G.ID",new{raw});
    var saved=await production.SaveDraftAsync(new(null,null,ProductionOrderSources.Stock,null,ProductionExecutionTypes.InHouse,null,DateTime.Today,null,raw,finished,null,null,[employee],[new(null,1,"TEST","GD3 gold",gold.Code,gold.Name,1,100000,1,1000000,0,null,null)],[]),session);
    var issued=await production.PostMaterialIssueAsync(new(saved.Id,saved.RowVersion,DateTime.Now,null,null,null,[new(gold.MaterialId,2.5m,tag)]),session);
    await production.CompleteAsync(saved.Id,issued.RowVersion,session);
    var detail=(await new FirebirdProductionQueryRepository().GetOrderDetailAsync(saved.Id,branch))!;
    var item=new ManualFinishedProductItem("TEMP","TEMP","GD3 gold",null,gold.Code,gold.Code,"Chỉ",receivedWeight,0,receivedWeight,1000000,1200000,100000,150000,100000,"GD3","M1",null,tag,detail.Lines[0].Id);
    var loss=receivedWeight==1m?.2m:0m;
    var receipt=await production.PostManualFinishedReceiptAsync(new(DateTime.Today,finished,saved.Id,[item],tag,Guid.NewGuid(),[new(gold.Code,loss,employee,WorkerGoldDebtWorkerTypes.Employee)]),session);
    var payload=await c.ExecuteScalarAsync<string>("SELECT PAYLOAD FROM PRODUCTION_RECEIPT_REQUESTS WHERE DOCUMENT_ID=@DocumentId",receipt) ?? throw new Exception("Missing receipt snapshot");
    var debts=(await c.QueryAsync<long>("SELECT ID FROM WORKER_GOLD_DEBTS WHERE PRODUCTION_ORDER_ID=@Id",saved)).ToArray();
    Require(debts.Length==(receivedWeight==1m?1:0),"unexpected gold debt count");
    if(debts.Length==1)
    {
        var stockBefore=JsonSerializer.Serialize(await c.QueryAsync("SELECT MATERIAL_ID,LOCATION_ID,QUANTITY FROM STOCK_BALANCES ORDER BY MATERIAL_ID,LOCATION_ID"));
        var vouchersBefore=await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS");
        await workbench.RecordWorkerGoldRepaymentAsync(new(debts[0],.3m,DateTime.Today,tag),session);
        await workbench.RecordWorkerGoldRepaymentAsync(new(debts[0],1m,DateTime.Today,tag),session);
        var remaining=await c.ExecuteScalarAsync<decimal>("SELECT WEIGHT_CHI-WEIGHT_PAID_CHI FROM WORKER_GOLD_DEBTS WHERE ID=@id",new{id=debts[0]});
        Require(remaining==0,"gold repayment not complete");
        Require(stockBefore==JsonSerializer.Serialize(await c.QueryAsync("SELECT MATERIAL_ID,LOCATION_ID,QUANTITY FROM STOCK_BALANCES ORDER BY MATERIAL_ID,LOCATION_ID")) && vouchersBefore==await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS"),"gold repayment changed stock or cash");
    }
    return new{productionOrder=saved.Id,receipt.DocumentId,issued=2.5m,receivedWeight,loss,debtCount=debts.Length,snapshotRetained=payload.Contains("GoldSettlements"),repaymentStockAndCashUnchanged=debts.Length==1};
});
await Case("T15/legacy-return-dependents",async()=>
{
    var legacy=new FirebirdOutsourcedCraftService();
    var worker=await c.QueryFirstAsync<long>("SELECT ID FROM EXTERNAL_WORKERS WHERE COMPANY_ID=@company AND IS_ACTIVE=TRUE",new{company});
    var material=await c.QueryFirstAsync<long>("SELECT M.ID FROM MATERIALS M JOIN STOCK_BALANCES B ON B.MATERIAL_ID=M.ID WHERE B.LOCATION_ID=@raw AND B.QUANTITY>1 AND M.IS_ACTIVE=TRUE ORDER BY M.ID",new{raw});
    var issue=await legacy.CreateAndPostIssueAsync(new(DateTime.Today,raw,worker,null,"GD3","GD3",null,tag,tag,[new(material,1,tag)]),session);
    var holding=await c.ExecuteScalarAsync<long>("SELECT ID FROM EXTERNAL_WORKER_HOLDINGS WHERE ISSUE_DOCUMENT_ID=@issue",new{issue});
    var receipt=await legacy.CreateAndPostReturnAsync(new(issue,DateTime.Today,raw,"GD3","GD3",1000,0,[],tag,[new(holding,0,.8m,0,0,tag,"WORKER_COMPENSATION",.2m,5000,1000,tag)],[],false),session);
    var payable=await c.ExecuteScalarAsync<long>("SELECT ID FROM PAYABLE_DEBTS WHERE ORIGIN_DOCUMENT_TYPE='STOCK_DOCUMENT' AND ORIGIN_DOCUMENT_ID=@receipt",new{receipt});
    var receivable=await c.ExecuteScalarAsync<long>("SELECT ID FROM RECEIVABLE_DEBTS WHERE ORIGIN_DOCUMENT_TYPE='STOCK_DOCUMENT' AND ORIGIN_DOCUMENT_ID=@receipt",new{receipt});
    await workbench.SettleDebtAllocationsAsync(new([new("PAYABLE_DEBT",payable,500),new("RECEIVABLE_DEBT",receivable,500)],1000,0,0,fund,null,tag),session);
    var before=await c.ExecuteScalarAsync<decimal>("SELECT QUANTITY FROM STOCK_BALANCES WHERE MATERIAL_ID=@material AND LOCATION_ID=@raw",new{material,raw});
    await reversals.ReverseStockDocumentAsync(receipt,tag,session);
    var after=await c.ExecuteScalarAsync<decimal>("SELECT QUANTITY FROM STOCK_BALANCES WHERE MATERIAL_ID=@material AND LOCATION_ID=@raw",new{material,raw});
    var payableStatus=await c.ExecuteScalarAsync<string>("SELECT STATUS_CODE FROM PAYABLE_DEBTS WHERE ID=@payable",new{payable});
    var receivableStatus=await c.ExecuteScalarAsync<string>("SELECT STATUS_CODE FROM RECEIVABLE_DEBTS WHERE ID=@receivable",new{receivable});
    Require(payableStatus=="PARTIAL" && receivableStatus=="PARTIAL" && before==after,"legacy dependent divergence not reproduced");
    return new{issue,receipt,payable,receivable,payableStatus,receivableStatus,stockReversalDelta=after-before,expectedReturnedStockReversal=-.8m};
});
await Case("T04/card-checkout",async()=>
{
    var draft=await Sale(await Customer(),1000,1000,0);
    draft=draft with{Allocations=[new("CARD",1000,null,bank,null)]};
    var before=await Snapshot();var error=await Reject(()=>sales.PostAsync(draft,session));
    Require(error.Contains("CK_CV_METHOD") && before==await Snapshot(),"CARD checkout not rejected atomically");
    return new{error,rollback=true};
});
await Case("T05/payable-direct-cancel",async()=>
{
    var customer=await Customer();var order=await Order(customer,1000);
    var debt=await Payable(customer,order,1000);
    await new FirebirdPayableDebtRepository().PayAsync(new(debt,[new("CASH",1000,fund,null,null)],tag),session);
    var vid=await c.ExecuteScalarAsync<long>("SELECT A.CASH_VOUCHER_ID FROM PAYABLE_DEBT_PAYMENT_ALLOCATIONS A JOIN PAYABLE_DEBT_PAYMENTS P ON P.ID=A.DEBT_PAYMENT_ID WHERE P.DEBT_ID=@debt",new{debt});
    await vouchers.CancelVoucherAsync(vid,tag,session);
    var status=await c.ExecuteScalarAsync<string>("SELECT STATUS_CODE FROM PAYABLE_DEBTS WHERE ID=@debt",new{debt});Require(status=="PAID","direct payable cancel divergence absent");
    return new{debt,vid,debtStatus=status};
});
await Case("T09/debt-target-scope",async()=>
{
    var other=await c.QuerySingleAsync<long>("INSERT INTO BRANCHES(COMPANY_ID,CODE,NAME,IS_ACTIVE) VALUES(@company,@code,'GD3 debt branch',TRUE) RETURNING ID",new{company,code=tag+"B"});
    var customer=await Customer();var order=await Order(customer,1000,other);
    var receivable=await Receivable(customer,order,1000,other);var payable=await Payable(customer,order,1000,other);
    await workbench.SettleDebtAllocationsAsync(new([new("RECEIVABLE_DEBT",receivable,1000),new("PAYABLE_DEBT",payable,1000)],2000,0,0,fund,null,tag),session);
    var postedBranches=(await c.QueryAsync<long>("SELECT S.BRANCH_ID FROM FINANCIAL_SETTLEMENTS S JOIN FINANCIAL_SETTLEMENT_APPLICATIONS A ON A.SETTLEMENT_ID=S.ID WHERE (A.TARGET_TYPE='RECEIVABLE_DEBT' AND A.TARGET_ID=@receivable) OR (A.TARGET_TYPE='PAYABLE_DEBT' AND A.TARGET_ID=@payable)",new{receivable,payable})).ToArray();
    Require(postedBranches.Length==2 && postedBranches.All(x=>x==branch),"debt cross-branch not reproduced");
    return new{receivable,payable,sourceBranch=other,postedBranches};
});
await Case("T11/debt-audit-rollback",async()=>
{
    var order=await Order(await Customer(),2000);var before=await Snapshot();
    await c.ExecuteAsync("CREATE EXCEPTION E_GD3_AUDIT 'GD3 audit fault'");
    await c.ExecuteAsync("CREATE TRIGGER T_GD3_AUDIT FOR AUDIT_LOG ACTIVE BEFORE INSERT POSITION 0 AS BEGIN EXCEPTION E_GD3_AUDIT; END");
    string error;
    try{error=await Reject(()=>workbench.SettleDebtAllocationsAsync(new([new("SALES_ORDER",order,2000)],2000,0,0,fund,null,tag),session));Require(before==await Snapshot(),"workbench did not roll back");}
    finally{await c.ExecuteAsync("DROP TRIGGER T_GD3_AUDIT");await c.ExecuteAsync("DROP EXCEPTION E_GD3_AUDIT");}
    return new{order,error,rollback=true};
});
var output=Path.Combine(root,tag+".results.json");
await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{database=path,schema,tag,results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("RESULTS="+output);
Environment.ExitCode=results.Count==0 || results.Any(x=>JsonSerializer.Serialize(x).Contains("\"status\":\"ERROR\""))?1:0;

async Task Case(string id,Func<Task<object>> action)
{
    var only=args.Where(x=>x.StartsWith("--case=",StringComparison.Ordinal)).ToArray();
    if(only.Length>0 && !only.Any(x=>id.StartsWith(x[7..],StringComparison.Ordinal)))return;
    try{var evidence=await action();results.Add(new{id,status="OBSERVED",evidence});Console.WriteLine(id+" OBSERVED "+JsonSerializer.Serialize(evidence));}
    catch(Exception ex){results.Add(new{id,status="ERROR",error=ex.ToString()});Console.WriteLine(id+" ERROR "+ex.GetType().Name+": "+ex.Message);}
}
void Require(bool ok,string message){if(!ok)throw new Exception(message);}
async Task<string> Reject(Func<Task> action){try{await action();}catch(Exception ex)when(ex is FbException or InvalidOperationException or ArgumentException){return ex.GetType().Name+": "+ex.Message;}throw new Exception("Expected rejection but operation succeeded");}
async Task<long> Customer()=>await c.QuerySingleAsync<long>("INSERT INTO CUSTOMERS(COMPANY_ID,CREATED_BRANCH_ID,CUSTOMER_GROUP_ID,CODE,CUSTOMER_TYPE,NAME,OPENING_POINTS,IS_ACTIVE,IS_WALK_IN) SELECT @company,@branch,MIN(ID),@code,'INDIVIDUAL',@tag,0,TRUE,FALSE FROM CUSTOMER_GROUPS WHERE COMPANY_ID=@company RETURNING ID",new{company,branch,code=tag+Guid.NewGuid().ToString("N")[..8],tag});
async Task<long> Order(long customer,decimal amount,long? atBranch=null)=>await c.QuerySingleAsync<long>("INSERT INTO SALES_ORDERS(COMPANY_ID,BRANCH_ID,CODE,ORDER_TYPE,CUSTOMER_ID,ORDER_DATE,SALES_PERSON_ID,STATUS_CODE,SUBTOTAL,DISCOUNT_AMOUNT,TAX_AMOUNT,GRAND_TOTAL,CREATED_BY,UPDATED_BY) VALUES(@company,@atBranch,@code,'RETAIL',@customer,CURRENT_TIMESTAMP,@user,'COMPLETED',@amount,0,0,@amount,@user,@user) RETURNING ID",new{company,atBranch=atBranch??branch,code=tag+Guid.NewGuid().ToString("N")[..8],customer,user,amount});
async Task<long> Receivable(long customer,long order,decimal amount,long? atBranch=null)=>await c.QuerySingleAsync<long>("INSERT INTO RECEIVABLE_DEBTS(COMPANY_ID,BRANCH_ID,CODE,PARTY_TYPE,CUSTOMER_ID,ORIGIN_DOCUMENT_TYPE,ORIGIN_DOCUMENT_ID,DEBT_KIND,ORIGINAL_AMOUNT_VND,COLLECTED_AMOUNT_VND,REMAINING_AMOUNT_VND,STATUS_CODE) VALUES(@company,@atBranch,@code,'CUSTOMER',@customer,'SALES_ORDER',@order,'SALES',@amount,0,@amount,'OPEN') RETURNING ID",new{company,atBranch=atBranch??branch,code=tag+Guid.NewGuid().ToString("N")[..8],customer,order,amount});
async Task<long> Payable(long customer,long order,decimal amount,long? atBranch=null)=>await c.QuerySingleAsync<long>("INSERT INTO PAYABLE_DEBTS(COMPANY_ID,BRANCH_ID,CODE,PARTY_TYPE,CUSTOMER_ID,ORIGIN_DOCUMENT_TYPE,ORIGIN_DOCUMENT_ID,DEBT_KIND,ORIGINAL_AMOUNT_VND,PAID_AMOUNT_VND,REMAINING_AMOUNT_VND,STATUS_CODE) VALUES(@company,@atBranch,@code,'CUSTOMER',@customer,'SALES_ORDER',@order,'PURCHASE',@amount,0,@amount,'OPEN') RETURNING ID",new{company,atBranch=atBranch??branch,code=tag+Guid.NewGuid().ToString("N")[..8],customer,order,amount});
async Task<SalesCheckoutDraft> Sale(long customer,decimal total,decimal paid,decimal debt)
{
    var code=tag+Guid.NewGuid().ToString("N")[..8];
    var product=await c.QuerySingleAsync<long>("INSERT INTO FINISHED_PRODUCTS(COMPANY_ID,BRANCH_ID,CODE,BARCODE_VALUE,QR_VALUE,PRODUCT_NAME,GOLD_PURITY,TOTAL_WEIGHT,GOLD_WEIGHT,STONE_WEIGHT,LABOR_COST,MATERIAL_COST,NON_GOLD_MATERIAL_COST,TOTAL_COST,LOCATION_ID,STATUS_CODE,ROW_VERSION,RETAIL_GOLD_PRICE_ID) SELECT @company,@branch,@code,@code,@code,'GD3 product',GOLD_PURITY,1,1,0,0,1000,0,1000,@floor,'DISPLAYED',1,RETAIL_GOLD_PRICE_ID FROM FINISHED_PRODUCTS WHERE ID=@template RETURNING ID",new{company,branch,code,floor,template});
    return new(Guid.NewGuid(),DateTime.Today,customer,user,floor,null,0,[new(product,total,0,0,"GD3 price")],[],paid>0?[new("CASH",paid,fund,null,null)]:[],debt,tag);
}
async Task<long> LastSettlement(long order)=>await c.ExecuteScalarAsync<long>("SELECT MAX(SETTLEMENT_ID) FROM FINANCIAL_SETTLEMENT_APPLICATIONS WHERE TARGET_TYPE='SALES_ORDER' AND TARGET_ID=@order",new{order});
async Task<decimal> VoucherTotal(long sid)=>await c.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(V.AMOUNT_VND),0) FROM CASH_VOUCHERS V WHERE V.STATUS_CODE='POSTED' AND EXISTS(SELECT 1 FROM FINANCIAL_SETTLEMENT_ALLOCATIONS A WHERE A.SETTLEMENT_ID=@sid AND A.CASH_VOUCHER_ID=V.ID)",new{sid});
async Task<string> Snapshot()
{
    var parts=new List<string>();foreach(var table in new[]{"SALES_CHECKOUTS","SALES_ORDERS","ORDER_PAYMENTS","FINANCIAL_SETTLEMENTS","FINANCIAL_SETTLEMENT_ALLOCATIONS","FINANCIAL_SETTLEMENT_APPLICATIONS","CASH_VOUCHERS","RECEIVABLE_DEBTS","STOCK_DOCUMENTS","STOCK_MOVEMENTS","FINISHED_PRODUCT_MOVEMENTS","AUDIT_LOG"})parts.Add(table+":"+await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM "+table));
    parts.Add(JsonSerializer.Serialize(await c.QueryAsync("SELECT ID,STATUS_CODE,ROW_VERSION,LOCATION_ID FROM FINISHED_PRODUCTS WHERE CODE STARTING WITH @tag ORDER BY ID",new{tag})));
    return string.Join("|",parts);
}
static void Sta(Action action){Exception? failure=null;var t=new Thread(()=>{try{action();}catch(Exception ex){failure=ex;}});t.SetApartmentState(ApartmentState.STA);t.Start();t.Join();if(failure is not null)throw failure;}

sealed class CaptureVouchers(Action<ManualVoucherRequest> capture):IFinanceVoucherService
{
    public Task<CashVoucherDetail> CreateManualVoucherAsync(ManualVoucherRequest r,AuthenticatedSession s,CancellationToken ct=default){capture(r);return Task.FromResult(new CashVoucherDetail(1,"GD3",DateTime.Today,"IN",r.PaymentMethod,r.AmountVnd,null,null,null,null,null,"GD3","POSTED",null,null));}
    public Task CancelVoucherAsync(long id,string reason,AuthenticatedSession s,CancellationToken ct=default)=>Task.CompletedTask;
}
