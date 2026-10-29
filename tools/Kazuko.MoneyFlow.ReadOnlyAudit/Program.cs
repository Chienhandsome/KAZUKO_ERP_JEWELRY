using System.Globalization;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Infrastructure.Data;

if(args.Length<2)throw new ArgumentException("Explicit DB path and output directory required; optional --test or --probe.");
var repo=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
var db=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);
var live=Path.Combine(repo,"data","DATA.FDB");
var test=args.Contains("--test");
if(!File.Exists(db))throw new FileNotFoundException("Database does not exist.");
if(test)
{
    if(!string.Equals(Path.GetDirectoryName(db),@"D:\Working\artifacts\money-flow-gd3",StringComparison.OrdinalIgnoreCase)
       || !Path.GetFileName(db).StartsWith("test91_moneyflow_",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid isolated test target.");
}
else if(!string.Equals(db,live,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Only this workspace DATA.FDB is allowed without --test.");
if(!output.StartsWith(@"D:\Working\artifacts\money-flow-gd4\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Output must be a new subdirectory of money-flow-gd4.");
if(Directory.Exists(output))throw new ArgumentException("Refusing to overwrite an audit output directory.");
var profile=ConnectionProfileStore.Load(live);
if(!test && (!string.Equals(Path.GetFullPath(profile.DatabasePath),live,StringComparison.OrdinalIgnoreCase)
    || !(profile.Host is "localhost" or "127.0.0.1")))throw new InvalidOperationException("Configured DB differs from reviewed target; inspect configuration first.");
// No application service calls, no profile persistence, and no business mutations.
await using var c=new FbConnection(FirebirdDatabase.BuildConnectionString(profile with{Host="localhost",DatabasePath=db,UseEmbedded=false}));
await c.OpenAsync();
await using var tx=await c.BeginTransactionAsync(new FbTransactionOptions{TransactionBehavior=FbTransactionBehavior.Read|FbTransactionBehavior.Concurrency|FbTransactionBehavior.Wait,WaitTimeout=TimeSpan.FromSeconds(5)});
var readOnly=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT MON$READ_ONLY FROM MON$TRANSACTIONS WHERE MON$TRANSACTION_ID=CURRENT_TRANSACTION",transaction:tx));
if(readOnly!=1)throw new InvalidOperationException("Firebird did not confirm a READ ONLY transaction.");
var snapshot=await c.QuerySingleAsync(new CommandDefinition("SELECT CURRENT_TIMESTAMP AS DB_TIME,CURRENT_TRANSACTION AS TRANSACTION_ID,CURRENT_CONNECTION AS CONNECTION_ID FROM RDB$DATABASE",transaction:tx));
var schema=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS",transaction:tx));
var catalog=new Dictionary<string,string>
{
 ["CASH_VOUCHERS"]="ID,COMPANY_ID,BRANCH_ID,CODE,VOUCHER_DATE,VOUCHER_TYPE,PAYMENT_METHOD,AMOUNT_VND,CASH_FUND_ID,BANK_ACCOUNT_ID,SOURCE_DOCUMENT_TYPE,SOURCE_DOCUMENT_ID,SOURCE_ALLOCATION_TYPE,SOURCE_ALLOCATION_ID,STATUS_CODE,CANCELLED_AT",
 ["FINANCIAL_SETTLEMENTS"]="ID,COMPANY_ID,BRANCH_ID,CODE,SETTLEMENT_TYPE,PARTY_TYPE,PARTY_ID,SETTLEMENT_DATE,TOTAL_AMOUNT_VND,STATUS_CODE,CANCELLED_AT",
 ["FINANCIAL_SETTLEMENT_ALLOCATIONS"]="ID,SETTLEMENT_ID,LINE_NUMBER,PAYMENT_METHOD,AMOUNT_VND,CASH_FUND_ID,BANK_ACCOUNT_ID,CASH_VOUCHER_ID",
 ["FINANCIAL_SETTLEMENT_APPLICATIONS"]="ID,SETTLEMENT_ID,TARGET_TYPE,TARGET_ID,AMOUNT_VND",
 ["SALES_ORDERS"]="ID,COMPANY_ID,BRANCH_ID,CODE,ORDER_TYPE,CUSTOMER_ID,ORDER_DATE,STATUS_CODE,GRAND_TOTAL,FINISHED_GOODS_ISSUE_DOCUMENT_ID",
 ["ORDER_PAYMENTS"]="ID,ORDER_ID,PAYMENT_DATE,PAYMENT_TYPE,AMOUNT,PAYMENT_METHOD,REFERENCE_NO,IS_VOIDED,FINANCIAL_SETTLEMENT_ID,CASH_VOUCHER_ID,CREATED_AT",
 ["SALES_CHECKOUTS"]="ID,COMPANY_ID,BRANCH_ID,CODE,DOCUMENT_DATE,CHECKOUT_TYPE,CUSTOMER_ID,SALE_GROSS_VND,OLD_GOLD_TOTAL_VND,DISCOUNT_AMOUNT_VND,NET_AMOUNT_VND,SALES_ORDER_ID,OLD_GOLD_STOCK_DOCUMENT_ID,FINANCIAL_SETTLEMENT_ID,RECEIVABLE_DEBT_ID,STATUS_CODE",
 ["SALES_CHECKOUT_OFFSETS"]="CHECKOUT_ID,SALES_ORDER_ID,AMOUNT_VND",
 ["RECEIVABLE_DEBTS"]="ID,COMPANY_ID,BRANCH_ID,CODE,PARTY_TYPE,CUSTOMER_ID,SUPPLIER_ID,EXTERNAL_WORKER_ID,ORIGIN_DOCUMENT_TYPE,ORIGIN_DOCUMENT_ID,DEBT_KIND,ORIGINAL_AMOUNT_VND,COLLECTED_AMOUNT_VND,REMAINING_AMOUNT_VND,STATUS_CODE",
 ["RECEIVABLE_DEBT_COLLECTIONS"]="ID,DEBT_ID,COLLECTION_DATE,AMOUNT_VND,SETTLEMENT_ID",
 ["PAYABLE_DEBTS"]="ID,COMPANY_ID,BRANCH_ID,CODE,PARTY_TYPE,CUSTOMER_ID,SUPPLIER_ID,EXTERNAL_WORKER_ID,ORIGIN_DOCUMENT_TYPE,ORIGIN_DOCUMENT_ID,DEBT_KIND,ORIGINAL_AMOUNT_VND,PAID_AMOUNT_VND,REMAINING_AMOUNT_VND,STATUS_CODE",
 ["PAYABLE_DEBT_PAYMENTS"]="ID,DEBT_ID,PAYMENT_DATE,AMOUNT_VND",
 ["PAYABLE_DEBT_PAYMENT_ALLOCATIONS"]="ID,DEBT_PAYMENT_ID,AMOUNT_VND,CASH_VOUCHER_ID",
 ["STOCK_DOCUMENTS"]="ID,COMPANY_ID,BRANCH_ID,CODE,DOCUMENT_TYPE,DOCUMENT_DATE,STATUS_CODE,SOURCE_ISSUE_DOCUMENT_ID,TOTAL_AMOUNT_VND,PAID_AMOUNT_VND,DEBT_AMOUNT_VND",
 ["STOCK_DOCUMENT_PAYMENTS"]="ID,DOCUMENT_ID,AMOUNT_VND,CASH_VOUCHER_ID",
 ["STOCK_CRAFT_SETTLEMENT_LINES"]="ID,RETURN_DOCUMENT_ID,HOLDING_ID,QUANTITY_RETURNED,QUANTITY_SCRAP,QUANTITY_IN_PRODUCT,QUANTITY_LOSS,RECEIVABLE_DEBT_ID",
 ["STOCK_MOVEMENTS"]="ID,DOCUMENT_ID,MATERIAL_ID,LOCATION_ID,QUANTITY_CHANGE,REVERSAL_OF_ID",
 ["DOCUMENT_REVERSALS"]="ID,COMPANY_ID,BRANCH_ID,SOURCE_DOCUMENT_TYPE,SOURCE_DOCUMENT_ID,REVERSED_AT",
 ["CASH_FUNDS"]="ID,COMPANY_ID,BRANCH_ID,IS_ACTIVE",
 ["BANK_ACCOUNTS"]="ID,COMPANY_ID,BRANCH_ID,IS_ACTIVE",
 ["HR_PAYROLL_PAYMENTS"]="ID,COMPANY_ID,BRANCH_ID,PERIOD_ID,EMPLOYEE_ID,AMOUNT,STATUS,VOUCHER_ID,PAYMENT_DATE",
 ["HR_PAYROLL_LINES"]="ID,PERIOD_ID,EMPLOYEE_ID,SALARY,BONUS,DEDUCTION,RECORDED_BONUS,RECORDED_DEDUCTION",
 ["HR_EMPLOYEE_AWARDS"]="ID,COMPANY_ID,BRANCH_ID,EMPLOYEE_ID,KIND,HANDLING,AMOUNT,STATUS,VOUCHER_ID",
 ["WORKER_GOLD_DEBTS"]="ID,COMPANY_ID,BRANCH_ID,WORKER_ID,WORKER_TYPE,GOLD_PURITY_CODE,WEIGHT_CHI,WEIGHT_PAID_CHI,STATUS_CODE,PRODUCTION_ORDER_ID",
 ["WORKER_GOLD_DEBT_PAYMENTS"]="ID,COMPANY_ID,BRANCH_ID,DEBT_ID,WEIGHT_PAID_CHI,PAID_DATE",
 ["PARTY_CREDITS"]="ID,COMPANY_ID,BRANCH_ID,ORIGINAL_AMOUNT_VND,USED_AMOUNT_VND,REMAINING_AMOUNT_VND,STATUS_CODE",
 ["AUDIT_LOG"]="ID,EVENT_TYPE,ENTITY_TYPE,ENTITY_ID"
};
var tables=new Dictionary<string,List<Row>>();var sqlLog=new List<string>();
var errors=new List<object>();
foreach(var (table,columns) in catalog)
{
    var sql=$"SELECT {columns} FROM {table}";sqlLog.Add(sql+";");
    try{tables[table]=(await c.QueryAsync(new CommandDefinition(sql,transaction:tx,commandTimeout:30))).Select(x=>new Row((IDictionary<string,object>)x)).ToList();}
    catch(Exception ex){errors.Add(new{table,error=ex.Message});tables[table]=[];}
}
var constraints=(await c.QueryAsync(new CommandDefinition("SELECT TRIM(C.RDB$CONSTRAINT_NAME) AS CONSTRAINT_NAME,TRIM(T.RDB$TRIGGER_SOURCE) AS SOURCE FROM RDB$CHECK_CONSTRAINTS C JOIN RDB$TRIGGERS T ON T.RDB$TRIGGER_NAME=C.RDB$TRIGGER_NAME WHERE C.RDB$CONSTRAINT_NAME IN ('CK_CV_METHOD','CK_ORDER_PAYMENT_METHOD')",transaction:tx))).Select(x=>new Row((IDictionary<string,object>)x)).ToArray();
Directory.CreateDirectory(output);
var metadata=new{db,test,readOnly,snapshot,schema,startedBangkok=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)),counts=tables.ToDictionary(x=>x.Key,x=>x.Value.Count),constraints,errors};
if(args.Contains("--probe"))
{
    await File.WriteAllTextAsync(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(metadata,JsonOptions()));
    Console.WriteLine(JsonSerializer.Serialize(metadata,JsonOptions()));await tx.RollbackAsync();Environment.ExitCode=errors.Count>0?1:0;return;
}
if(errors.Count>0)throw new InvalidOperationException("Audit inputs incomplete; run --probe and resolve schema columns first.");
var sections=new Dictionary<string,object>();
List<Row> T(string name)=>tables[name];
Dictionary<long,Row> Idx(string name)=>T(name).ToDictionary(x=>x.L("ID"));
var vs=Idx("CASH_VOUCHERS");var ss=Idx("FINANCIAL_SETTLEMENTS");var os=Idx("SALES_ORDERS");
var rd=Idx("RECEIVABLE_DEBTS");var pd=Idx("PAYABLE_DEBTS");var stock=Idx("STOCK_DOCUMENTS");var checks=Idx("SALES_CHECKOUTS");
var alloc=T("FINANCIAL_SETTLEMENT_ALLOCATIONS");var apps=T("FINANCIAL_SETTLEMENT_APPLICATIONS");var ops=T("ORDER_PAYMENTS");
var colls=T("RECEIVABLE_DEBT_COLLECTIONS");var offsets=T("SALES_CHECKOUT_OFFSETS");
bool Posted(Row r)=>r.S("STATUS_CODE")=="POSTED";
bool Open(Row r)=>r.S("STATUS_CODE") is not ("DRAFT" or "CANCELLED");
decimal Sign(Row v)=>v.S("VOUCHER_TYPE")=="IN"?1m:-1m;
decimal Paid(long id)=>ops.Where(p=>p.L("ORDER_ID")==id&&!p.B("IS_VOIDED")).Sum(p=>p.D("AMOUNT")*(p.S("PAYMENT_TYPE")=="REFUND"?-1:1));
decimal Offset(long id)=>offsets.Where(x=>x.L("SALES_ORDER_ID")==id&&checks.TryGetValue(x.L("CHECKOUT_ID"),out var s)&&Posted(s)).Sum(x=>x.D("AMOUNT_VND"));
List<Row> LinkedVouchers(long sid)=>alloc.Where(x=>x.L("SETTLEMENT_ID")==sid).Select(x=>x.L("CASH_VOUCHER_ID")).Distinct().Where(vs.ContainsKey).Select(id=>vs[id]).ToList();
bool SettlementMoneyComplete(Row s)=>Posted(s)&&LinkedVouchers(s.L("ID")).Where(Posted).Sum(v=>v.D("AMOUNT_VND"))==s.D("TOTAL_AMOUNT_VND");

var header=new Dictionary<(long Company,long Branch,string Kind,long Target),decimal>();
var detail=new Dictionary<(long Company,long Branch,string Kind,long Target),decimal>();
void Add(Dictionary<(long,long,string,long),decimal> map,Row v,string kind,long target,decimal amount){var key=(v.L("COMPANY_ID"),v.L("BRANCH_ID"),kind,target);map[key]=map.GetValueOrDefault(key)+Sign(v)*amount;}
var fundExceptions=new List<object>();var amountMismatch=new List<object>();
foreach(var v in vs.Values.Where(Posted))
{
    var va=alloc.Where(a=>a.L("CASH_VOUCHER_ID")==v.L("ID")).ToList();
    string Place(Row r)=>r.L("CASH_FUND_ID")>0?"CASH:"+r.L("CASH_FUND_ID"):r.L("BANK_ACCOUNT_ID")>0?"BANK:"+r.L("BANK_ACCOUNT_ID"):"UNASSIGNED:0";
    Add(header,v,v.L("CASH_FUND_ID")>0?"CASH":v.L("BANK_ACCOUNT_ID")>0?"BANK":"UNASSIGNED",Math.Max(v.L("CASH_FUND_ID"),v.L("BANK_ACCOUNT_ID")),v.D("AMOUNT_VND"));
    if(va.Count==0)Add(detail,v,v.L("CASH_FUND_ID")>0?"CASH":v.L("BANK_ACCOUNT_ID")>0?"BANK":"UNASSIGNED",Math.Max(v.L("CASH_FUND_ID"),v.L("BANK_ACCOUNT_ID")),v.D("AMOUNT_VND"));
    else
    {
        foreach(var a in va)Add(detail,v,a.L("CASH_FUND_ID")>0?"CASH":a.L("BANK_ACCOUNT_ID")>0?"BANK":"UNASSIGNED",Math.Max(a.L("CASH_FUND_ID"),a.L("BANK_ACCOUNT_ID")),a.D("AMOUNT_VND"));
        if(va.Sum(a=>a.D("AMOUNT_VND"))!=v.D("AMOUNT_VND"))amountMismatch.Add(new{voucherId=v.L("ID"),headerAmount=v.D("AMOUNT_VND"),allocationSum=va.Sum(a=>a.D("AMOUNT_VND"))});
        if(va.Any(a=>Place(a)!=Place(v)))fundExceptions.Add(new{voucherId=v.L("ID"),code=v.S("CODE"),company=v.L("COMPANY_ID"),branch=v.L("BRANCH_ID"),direction=v.S("VOUCHER_TYPE"),amount=v.D("AMOUNT_VND"),headerPlace=Place(v),allocations=va.Select(a=>new{allocationId=a.L("ID"),place=Place(a),amount=a.D("AMOUNT_VND")})});
    }
}
foreach(var name in new[]{"CASH_FUNDS","BANK_ACCOUNTS"})foreach(var target in T(name)){var k=(target.L("COMPANY_ID"),target.L("BRANCH_ID"),name=="CASH_FUNDS"?"CASH":"BANK",target.L("ID"));header.TryAdd(k,0);detail.TryAdd(k,0);}
var fundBalances=header.Keys.Union(detail.Keys).Select(k=>new{company=k.Company,branch=k.Branch,kind=k.Kind,target=k.Target,headerBalance=header.GetValueOrDefault(k),allocationBalance=detail.GetValueOrDefault(k),difference=detail.GetValueOrDefault(k)-header.GetValueOrDefault(k)}).ToArray();
sections["R01_funds"]=new{postedVouchers=vs.Values.Count(Posted),income=vs.Values.Where(v=>Posted(v)&&v.S("VOUCHER_TYPE")=="IN").Sum(v=>v.D("AMOUNT_VND")),expense=vs.Values.Where(v=>Posted(v)&&v.S("VOUCHER_TYPE")=="OUT").Sum(v=>v.D("AMOUNT_VND")),fundBalances,exceptions=fundExceptions,amountMismatch};

var settlementExceptions=new List<object>();var activeSettlementCancelledVouchers=new List<object>();
foreach(var s in ss.Values)
{
    var sid=s.L("ID");var linked=LinkedVouchers(sid);var sumA=alloc.Where(a=>a.L("SETTLEMENT_ID")==sid).Sum(a=>a.D("AMOUNT_VND"));var sumApp=apps.Where(a=>a.L("SETTLEMENT_ID")==sid).Sum(a=>a.D("AMOUNT_VND"));var effective=linked.Where(Posted).Sum(v=>v.D("AMOUNT_VND"));
    if(Posted(s)&&(sumA!=s.D("TOTAL_AMOUNT_VND")||sumApp!=s.D("TOTAL_AMOUNT_VND")||effective!=s.D("TOTAL_AMOUNT_VND"))||!Posted(s)&&effective!=0)
        settlementExceptions.Add(new{settlementId=sid,code=s.S("CODE"),status=s.S("STATUS_CODE"),total=s.D("TOTAL_AMOUNT_VND"),sumA,sumApp,postedVoucherTotal=effective,voucherIds=linked.Select(v=>v.L("ID"))});
    if(Posted(s))foreach(var v in linked.Where(v=>!Posted(v)))activeSettlementCancelledVouchers.Add(new{settlementId=sid,voucherId=v.L("ID"),voucherStatus=v.S("STATUS_CODE"),amount=v.D("AMOUNT_VND")});
}
sections["R02_settlements"]=new{total=ss.Count,posted=ss.Values.Count(Posted),exceptions=settlementExceptions,activeSettlementCancelledVouchers};

var overlaps=new List<object>();var synchronization=new List<object>();var overcollection=new List<object>();var offsetExposure=new List<object>();
foreach(var o in os.Values.Where(Open))
{
    var oid=o.L("ID");var paid=Paid(oid);var offset=Offset(oid);var remaining=o.D("GRAND_TOTAL")-paid-offset;
    var debts=rd.Values.Where(r=>Open(r)&&r.S("ORIGIN_DOCUMENT_TYPE")=="SALES_ORDER"&&r.L("ORIGIN_DOCUMENT_ID")==oid).ToArray();
    var correct=debts.Where(r=>r.L("COMPANY_ID")==o.L("COMPANY_ID")&&r.L("BRANCH_ID")==o.L("BRANCH_ID")&&r.L("CUSTOMER_ID")==o.L("CUSTOMER_ID")).ToArray();
    var dr=correct.Sum(r=>r.D("REMAINING_AMOUNT_VND"));
    if(remaining>0&&dr>0)overlaps.Add(new{orderId=oid,orderCode=o.S("CODE"),company=o.L("COMPANY_ID"),branch=o.L("BRANCH_ID"),customerId=o.L("CUSTOMER_ID"),orderRemaining=remaining,receivableRemaining=dr,duplicateOverlap=Math.Min(remaining,dr),debtIds=correct.Select(r=>r.L("ID"))});
    if(correct.Length>0&&dr!=Math.Max(remaining,0))synchronization.Add(new{orderId=oid,orderRemaining=remaining,receivableRemaining=dr,difference=dr-Math.Max(remaining,0),debtIds=correct.Select(r=>r.L("ID"))});
    if(offset>0&&o.D("GRAND_TOTAL")-paid>0)offsetExposure.Add(new{orderId=oid,offset,backendOffered=o.D("GRAND_TOTAL")-paid,afterOffsetRemaining=remaining,excessOffer=Math.Min(offset,o.D("GRAND_TOTAL")-paid)});
    var debtIds=correct.Select(r=>r.L("ID")).ToHashSet();
    var moneyApps=apps.Where(a=>(a.S("TARGET_TYPE")=="SALES_ORDER"&&a.L("TARGET_ID")==oid||a.S("TARGET_TYPE")=="RECEIVABLE_DEBT"&&debtIds.Contains(a.L("TARGET_ID")))&&ss.TryGetValue(a.L("SETTLEMENT_ID"),out var s)&&SettlementMoneyComplete(s)).ToArray();
    var appCash=moneyApps.Sum(a=>a.D("AMOUNT_VND"));
    if(appCash+offset>o.D("GRAND_TOTAL"))overcollection.Add(new{orderId=oid,orderValue=o.D("GRAND_TOTAL"),offset,completeReceiptApplications=appCash,excess=appCash+offset-o.D("GRAND_TOTAL"),settlementIds=moneyApps.Select(a=>a.L("SETTLEMENT_ID")).Distinct(),hasRefundRows=ops.Any(p=>p.L("ORDER_ID")==oid&&!p.B("IS_VOIDED")&&p.S("PAYMENT_TYPE")=="REFUND")});
}
sections["R03_order_receivable"]=new{overlaps,synchronization,overcollectionCandidates=overcollection,offsetExposure};

var stalePayments=new List<object>();var paymentCancelledVoucher=new List<object>();var unresolvedPaymentLinks=new List<object>();
foreach(var p in ops.Where(p=>!p.B("IS_VOIDED")))
{
    var sid=p.L("FINANCIAL_SETTLEMENT_ID");
    var linked=sid>0&&ss.TryGetValue(sid,out var direct)?direct:ss.Values.FirstOrDefault(s=>s.S("CODE")==p.S("REFERENCE_NO")&&apps.Any(a=>a.L("SETTLEMENT_ID")==s.L("ID")&&a.S("TARGET_TYPE")=="SALES_ORDER"&&a.L("TARGET_ID")==p.L("ORDER_ID")));
    if(linked is not null&&!Posted(linked))stalePayments.Add(new{paymentId=p.L("ID"),orderId=p.L("ORDER_ID"),settlementId=linked.L("ID"),settlementStatus=linked.S("STATUS_CODE"),amount=p.D("AMOUNT")});
    if(vs.TryGetValue(p.L("CASH_VOUCHER_ID"),out var v)&&!Posted(v))paymentCancelledVoucher.Add(new{paymentId=p.L("ID"),orderId=p.L("ORDER_ID"),voucherId=v.L("ID"),amount=p.D("AMOUNT")});
    if(linked is null&&p.L("CASH_VOUCHER_ID")==0)unresolvedPaymentLinks.Add(new{paymentId=p.L("ID"),orderId=p.L("ORDER_ID"),amount=p.D("AMOUNT"),method=p.S("PAYMENT_METHOD")});
}
sections["R04_payments"]=new{stalePayments,paymentCancelledVoucher,unresolvedPaymentLinks};

var scopeExceptions=new List<object>();
foreach(var a in apps)
{
    if(!ss.TryGetValue(a.L("SETTLEMENT_ID"),out var s))continue;
    var target=a.S("TARGET_TYPE")=="SALES_ORDER"?os.GetValueOrDefault(a.L("TARGET_ID")):a.S("TARGET_TYPE")=="RECEIVABLE_DEBT"?rd.GetValueOrDefault(a.L("TARGET_ID")):pd.GetValueOrDefault(a.L("TARGET_ID"));
    if(target is null||target.L("COMPANY_ID")!=s.L("COMPANY_ID")||target.L("BRANCH_ID")!=s.L("BRANCH_ID"))scopeExceptions.Add(new{applicationId=a.L("ID"),settlementId=s.L("ID"),status=s.S("STATUS_CODE"),type=a.S("TARGET_TYPE"),targetId=a.L("TARGET_ID"),settlementCompany=s.L("COMPANY_ID"),settlementBranch=s.L("BRANCH_ID"),targetCompany=target?.L("COMPANY_ID"),targetBranch=target?.L("BRANCH_ID"),amount=a.D("AMOUNT_VND")});
}
sections["R05_scope"]=scopeExceptions;

var sourceCancelled=new List<object>();var directCancelled=new List<object>();
foreach(var v in vs.Values)
{
    Row? source=v.S("SOURCE_DOCUMENT_TYPE") switch{"STOCK_DOCUMENT"=>stock.GetValueOrDefault(v.L("SOURCE_DOCUMENT_ID")),"PAYABLE_DEBT"=>pd.GetValueOrDefault(v.L("SOURCE_DOCUMENT_ID")),"FINANCIAL_SETTLEMENT"=>ss.GetValueOrDefault(v.L("SOURCE_DOCUMENT_ID")),_=>null};
    if(source is not null&&Posted(v)&&!Open(source))sourceCancelled.Add(new{voucherId=v.L("ID"),sourceType=v.S("SOURCE_DOCUMENT_TYPE"),sourceId=v.L("SOURCE_DOCUMENT_ID"),sourceStatus=source.S("STATUS_CODE"),amount=v.D("AMOUNT_VND")});
    if(!Posted(v)&&source is not null&&Open(source)&&v.S("SOURCE_DOCUMENT_TYPE")!="FINANCIAL_SETTLEMENT")directCancelled.Add(new{voucherId=v.L("ID"),sourceType=v.S("SOURCE_DOCUMENT_TYPE"),sourceId=v.L("SOURCE_DOCUMENT_ID"),sourceStatus=source.S("STATUS_CODE"),amount=v.D("AMOUNT_VND")});
}
var cancelledStockDebts=new List<object>();
foreach(var name in new[]{"PAYABLE_DEBTS","RECEIVABLE_DEBTS"})foreach(var d in T(name).Where(Open))
    if(d.S("ORIGIN_DOCUMENT_TYPE")=="STOCK_DOCUMENT"&&stock.TryGetValue(d.L("ORIGIN_DOCUMENT_ID"),out var s)&&!Open(s))cancelledStockDebts.Add(new{ledger=name,debtId=d.L("ID"),sourceId=s.L("ID"),sourceType=s.S("DOCUMENT_TYPE"),debtStatus=d.S("STATUS_CODE"),remaining=d.D("REMAINING_AMOUNT_VND"),paid=d.D(name=="PAYABLE_DEBTS"?"PAID_AMOUNT_VND":"COLLECTED_AMOUNT_VND")});
var legacyReturned=T("STOCK_CRAFT_SETTLEMENT_LINES").Where(x=>stock.TryGetValue(x.L("RETURN_DOCUMENT_ID"),out var s)&&!Open(s)&&(x.D("QUANTITY_RETURNED")>0||x.D("QUANTITY_SCRAP")>0)).Select(x=>new{receiptId=x.L("RETURN_DOCUMENT_ID"),holdingId=x.L("HOLDING_ID"),returned=x.D("QUANTITY_RETURNED"),scrap=x.D("QUANTITY_SCRAP"),movementRows=T("STOCK_MOVEMENTS").Count(m=>m.L("DOCUMENT_ID")==x.L("RETURN_DOCUMENT_ID"))}).ToArray();
sections["R06_owners"]=new{sourceCancelled,directCancelled,cancelledStockDebts,legacyReturned};

var ledgerMismatch=new List<object>();
foreach(var name in new[]{"PAYABLE_DEBTS","RECEIVABLE_DEBTS"})foreach(var d in T(name))
{
    var paid=d.D(name=="PAYABLE_DEBTS"?"PAID_AMOUNT_VND":"COLLECTED_AMOUNT_VND");
    if(d.D("ORIGINAL_AMOUNT_VND")!=paid+d.D("REMAINING_AMOUNT_VND"))ledgerMismatch.Add(new{ledger=name,debtId=d.L("ID"),original=d.D("ORIGINAL_AMOUNT_VND"),paid,remaining=d.D("REMAINING_AMOUNT_VND")});
}
var collectionMismatch=new List<object>();
foreach(var d in rd.Values.Where(Open))
{
    var collections=colls.Where(x=>x.L("DEBT_ID")==d.L("ID")).ToArray();var effective=collections.Where(x=>ss.TryGetValue(x.L("SETTLEMENT_ID"),out var s)&&Posted(s)).Sum(x=>x.D("AMOUNT_VND"));
    if(effective!=d.D("COLLECTED_AMOUNT_VND"))collectionMismatch.Add(new{debtId=d.L("ID"),collected=d.D("COLLECTED_AMOUNT_VND"),effectiveCollections=effective,unknownSettlementCollections=collections.Count(x=>!ss.ContainsKey(x.L("SETTLEMENT_ID")))});
}
var staleHistory=T("PAYABLE_DEBT_PAYMENTS").Where(p=>pd.TryGetValue(p.L("DEBT_ID"),out var d)&&d.D("PAID_AMOUNT_VND")==0&&d.S("STATUS_CODE")=="OPEN").Select(p=>new{paymentId=p.L("ID"),debtId=p.L("DEBT_ID"),amount=p.D("AMOUNT_VND")}).ToArray();
sections["R07_debt_ledgers"]=new{ledgerMismatch,collectionMismatch,stalePayableHistoryCandidates=staleHistory};

var hrExceptions=new List<object>();
foreach(var p in T("HR_PAYROLL_PAYMENTS"))
{
    var v=vs.GetValueOrDefault(p.L("VOUCHER_ID"));var active=p.S("STATUS")=="POSTED";
    var line=T("HR_PAYROLL_LINES").FirstOrDefault(l=>l.L("PERIOD_ID")==p.L("PERIOD_ID")&&l.L("EMPLOYEE_ID")==p.L("EMPLOYEE_ID"));
    var net=line is null?(decimal?)null:line.D("SALARY")+line.D("BONUS")+line.D("RECORDED_BONUS")-line.D("DEDUCTION")-line.D("RECORDED_DEDUCTION");
    if(active&&(v is null||!Posted(v)||v.D("AMOUNT_VND")!=p.D("AMOUNT")||net!=p.D("AMOUNT"))||!active&&v is not null&&Posted(v))hrExceptions.Add(new{paymentId=p.L("ID"),period=p.L("PERIOD_ID"),employee=p.L("EMPLOYEE_ID"),paymentStatus=p.S("STATUS"),voucherId=p.L("VOUCHER_ID"),voucherStatus=v?.S("STATUS_CODE"),amount=p.D("AMOUNT"),net});
}
var duplicatePayroll=T("HR_PAYROLL_PAYMENTS").Where(p=>p.S("STATUS")=="POSTED").GroupBy(p=>(p.L("PERIOD_ID"),p.L("EMPLOYEE_ID"))).Where(g=>g.Count()>1).Select(g=>new{period=g.Key.Item1,employee=g.Key.Item2,ids=g.Select(p=>p.L("ID"))}).ToArray();
var awardExceptions=T("HR_EMPLOYEE_AWARDS").Where(a=>a.S("HANDLING")=="CASH").Where(a=>!vs.TryGetValue(a.L("VOUCHER_ID"),out var v)||(a.S("STATUS")=="ACTIVE")!=Posted(v)||a.D("AMOUNT")!=v.D("AMOUNT_VND")).Select(a=>new{awardId=a.L("ID"),status=a.S("STATUS"),voucherId=a.L("VOUCHER_ID"),amount=a.D("AMOUNT")}).ToArray();
sections["R08_HR"]=new{hrExceptions,duplicatePayroll,awardExceptions};

var goldExceptions=new List<object>();
foreach(var d in T("WORKER_GOLD_DEBTS"))
{
    var sum=T("WORKER_GOLD_DEBT_PAYMENTS").Where(p=>p.L("DEBT_ID")==d.L("ID")).Sum(p=>p.D("WEIGHT_PAID_CHI"));
    if(sum!=d.D("WEIGHT_PAID_CHI")||d.D("WEIGHT_PAID_CHI")>d.D("WEIGHT_CHI"))goldExceptions.Add(new{debtId=d.L("ID"),workerId=d.L("WORKER_ID"),gold=d.S("GOLD_PURITY_CODE"),original=d.D("WEIGHT_CHI"),paid=d.D("WEIGHT_PAID_CHI"),paymentSum=sum});
}
sections["R09_gold_credits"]=new{goldExceptions,goldRepayments=T("WORKER_GOLD_DEBT_PAYMENTS").Count,goldPaidChi=T("WORKER_GOLD_DEBT_PAYMENTS").Sum(p=>p.D("WEIGHT_PAID_CHI")),creditMismatch=T("PARTY_CREDITS").Where(x=>x.D("ORIGINAL_AMOUNT_VND")!=x.D("USED_AMOUNT_VND")+x.D("REMAINING_AMOUNT_VND")).Select(x=>x.L("ID")).ToArray()};
var cancelled=vs.Values.Where(v=>v.S("STATUS_CODE")=="CANCELLED").Select(v=>new{voucherId=v.L("ID"),source=v.S("SOURCE_DOCUMENT_TYPE"),amount=v.D("AMOUNT_VND"),cancelledAt=v.GetValueOrDefault("CANCELLED_AT"),voucherSpecificAuditEvents=T("AUDIT_LOG").Where(a=>a.S("ENTITY_ID")==v.L("ID").ToString(CultureInfo.InvariantCulture)&&a.S("ENTITY_TYPE") is "CASH_VOUCHERS" or "CASH_VOUCHER").Select(a=>a.S("EVENT_TYPE")),ownerCancellationEvents=T("AUDIT_LOG").Where(a=>a.S("ENTITY_ID")==v.L("SOURCE_DOCUMENT_ID").ToString(CultureInfo.InvariantCulture)&&(a.S("ENTITY_TYPE")==v.S("SOURCE_DOCUMENT_TYPE")||v.S("SOURCE_DOCUMENT_TYPE")=="HR_PAYROLL"&&a.S("ENTITY_TYPE")=="HR_PAYROLL_PAYMENTS"||v.S("SOURCE_DOCUMENT_TYPE")=="HR_AWARD"&&a.S("ENTITY_TYPE")=="HR_EMPLOYEE_AWARDS")&&(a.S("EVENT_TYPE").Contains("REVERSED",StringComparison.Ordinal)||a.S("EVENT_TYPE").Contains("CANCELLED",StringComparison.Ordinal))).Select(a=>a.S("EVENT_TYPE"))}).ToArray();
sections["R10_audit_coverage"]=new{cancelled,manualPosted=vs.Values.Count(v=>Posted(v)&&v.S("SOURCE_DOCUMENT_TYPE")=="MANUAL"),manualTargetIntent="Not reconstructable from persisted request: manual contract does not save user-selected fund/bank."};
var retryCandidates=ops.Where(p=>!p.B("IS_VOIDED")).GroupBy(p=>(p.L("ORDER_ID"),p.D("AMOUNT"),p.S("PAYMENT_METHOD"),p.S("PAYMENT_DATE"))).Where(g=>g.Count()>1).Select(g=>new{orderId=g.Key.Item1,amount=g.Key.Item2,method=g.Key.Item3,date=g.Key.Item4,paymentIds=g.Select(p=>p.L("ID")),classification="candidate only; same amount/time does not prove duplicate intent"}).ToArray();
sections["R11_retry_candidates"]=retryCandidates;
sections["R12_baseline"]=new
{
    vouchers=vs.Values.GroupBy(v=>(v.L("COMPANY_ID"),v.L("BRANCH_ID"),v.S("STATUS_CODE"),v.S("PAYMENT_METHOD"),v.S("VOUCHER_TYPE"))).Select(g=>new{company=g.Key.Item1,branch=g.Key.Item2,status=g.Key.Item3,method=g.Key.Item4,direction=g.Key.Item5,count=g.Count(),amount=g.Sum(v=>v.D("AMOUNT_VND"))}),
    voucherDates=vs.Values.Select(v=>v.GetValueOrDefault("VOUCHER_DATE")),
    orders=os.Values.Select(o=>new{id=o.L("ID"),code=o.S("CODE"),company=o.L("COMPANY_ID"),branch=o.L("BRANCH_ID"),customer=o.L("CUSTOMER_ID"),type=o.S("ORDER_TYPE"),status=o.S("STATUS_CODE"),grandTotal=o.D("GRAND_TOTAL"),payments=Paid(o.L("ID")),offset=Offset(o.L("ID")),remaining=o.D("GRAND_TOTAL")-Paid(o.L("ID"))-Offset(o.L("ID"))}),
    payables=pd.Values.GroupBy(d=>(d.L("COMPANY_ID"),d.L("BRANCH_ID"),d.S("DEBT_KIND"),d.S("STATUS_CODE"))).Select(g=>new{company=g.Key.Item1,branch=g.Key.Item2,kind=g.Key.Item3,status=g.Key.Item4,count=g.Count(),original=g.Sum(x=>x.D("ORIGINAL_AMOUNT_VND")),paid=g.Sum(x=>x.D("PAID_AMOUNT_VND")),remaining=g.Sum(x=>x.D("REMAINING_AMOUNT_VND"))}),
    payroll=T("HR_PAYROLL_PAYMENTS").GroupBy(p=>p.S("STATUS")).Select(g=>new{status=g.Key,count=g.Count(),amount=g.Sum(p=>p.D("AMOUNT"))}),
    gold=T("WORKER_GOLD_DEBTS").GroupBy(d=>d.S("GOLD_PURITY_CODE")).Select(g=>new{gold=g.Key,count=g.Count(),originalChi=g.Sum(d=>d.D("WEIGHT_CHI")),paidChi=g.Sum(d=>d.D("WEIGHT_PAID_CHI")),remainingChi=g.Sum(d=>d.D("WEIGHT_CHI")-d.D("WEIGHT_PAID_CHI"))}),
    stockSources=stock.Values.GroupBy(s=>(s.S("DOCUMENT_TYPE"),s.S("STATUS_CODE"))).Select(g=>new{type=g.Key.Item1,status=g.Key.Item2,count=g.Count()}),
    note="Company/branch totals are separate. Book cash, debt, gold weight and stock values are not additive losses."
};
var checkoutExceptions=new List<object>();
foreach(var sc in checks.Values.Where(Posted))
{
    var id=sc.L("ID");var expectedNet=sc.D("SALE_GROSS_VND")-sc.D("DISCOUNT_AMOUNT_VND")-sc.D("OLD_GOLD_TOTAL_VND");
    var expectedOffset=Math.Min(sc.D("SALE_GROSS_VND")-sc.D("DISCOUNT_AMOUNT_VND"),sc.D("OLD_GOLD_TOTAL_VND"));
    var gotOffset=offsets.Where(x=>x.L("CHECKOUT_ID")==id).Sum(x=>x.D("AMOUNT_VND"));
    var receipt=ss.TryGetValue(sc.L("FINANCIAL_SETTLEMENT_ID"),out var se)&&Posted(se)?se.D("TOTAL_AMOUNT_VND"):0;
    var originalDebt=rd.TryGetValue(sc.L("RECEIVABLE_DEBT_ID"),out var d)?d.D("ORIGINAL_AMOUNT_VND"):0;
    var payout=T("STOCK_DOCUMENT_PAYMENTS").Where(p=>p.L("DOCUMENT_ID")==sc.L("OLD_GOLD_STOCK_DOCUMENT_ID")&&vs.TryGetValue(p.L("CASH_VOUCHER_ID"),out var v)&&Posted(v)&&v.S("VOUCHER_TYPE")=="OUT").Sum(p=>p.D("AMOUNT_VND"));
    if(expectedNet!=sc.D("NET_AMOUNT_VND")||expectedOffset!=gotOffset||expectedNet>=0&&receipt+originalDebt!=expectedNet||expectedNet<0&&payout!=-expectedNet)
        checkoutExceptions.Add(new{checkoutId=id,expectedNet,storedNet=sc.D("NET_AMOUNT_VND"),expectedOffset,gotOffset,receipt,originalDebt,payout});
}
var offeredSql="""
    SELECT O.ID, O.GRAND_TOTAL,
      O.GRAND_TOTAL - COALESCE((SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
        FROM ORDER_PAYMENTS P WHERE P.ORDER_ID=O.ID AND P.IS_VOIDED=FALSE),0) AS BACKEND_OFFERED,
      (SELECT COALESCE(SUM(X.AMOUNT_VND),0) FROM SALES_CHECKOUT_OFFSETS X WHERE X.SALES_ORDER_ID=O.ID) AS OFFSET_AMOUNT
    FROM SALES_ORDERS O WHERE O.STATUS_CODE NOT IN ('DRAFT','CANCELLED')
      AND EXISTS(SELECT 1 FROM SALES_CHECKOUT_OFFSETS X WHERE X.SALES_ORDER_ID=O.ID)
    """;
sqlLog.Add(offeredSql+";");
var offeredRows=await c.QueryAsync(new CommandDefinition(offeredSql,transaction:tx));
sections["R13_checkout"]=new{checkoutExceptions,offsetBackendSqlEvidence=offeredRows,offsetSources=offsets.Select(x=>new{orderId=x.L("SALES_ORDER_ID"),checkoutId=x.L("CHECKOUT_ID"),amount=x.D("AMOUNT_VND"),checkout=checks.GetValueOrDefault(x.L("CHECKOUT_ID"))})};
var migrationSql="SELECT VERSION_NO,CHECKSUM_VALUE AS RECORDED_HASH FROM SCHEMA_MIGRATIONS WHERE VERSION_NO>=74 ORDER BY VERSION_NO";
sqlLog.Add(migrationSql+";");
var migrations=(await c.QueryAsync(new CommandDefinition(migrationSql,transaction:tx))).Select(x=>new Row((IDictionary<string,object>)x)).Select(r=>
{
    var files=Directory.GetFiles(Path.Combine(repo,"database","migrations"),r.L("VERSION_NO").ToString("D4",CultureInfo.InvariantCulture)+"__*.sql");
    var file=files.SingleOrDefault();var local=file is null?null:Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
    return new{version=r.L("VERSION_NO"),recorded=r.S("RECORDED_HASH"),local,match=string.Equals(r.S("RECORDED_HASH"),local,StringComparison.OrdinalIgnoreCase)};
}).ToArray();
sections["R14_migrations"]=migrations;
var receiptEquations=new List<object>();var receiptEquationExceptions=new List<object>();
foreach(var s in stock.Values.Where(s=>Posted(s)&&s.S("DOCUMENT_TYPE")=="RECEIPT"))
{
    var sid=s.L("ID");
    var checkout=checks.Values.FirstOrDefault(sc=>sc.L("OLD_GOLD_STOCK_DOCUMENT_ID")==sid&&Posted(sc));
    var cash=T("STOCK_DOCUMENT_PAYMENTS").Where(p=>p.L("DOCUMENT_ID")==sid&&vs.TryGetValue(p.L("CASH_VOUCHER_ID"),out var v)&&Posted(v)).Sum(p=>p.D("AMOUNT_VND"));
    var debt=pd.Values.Where(d=>Open(d)&&d.S("ORIGIN_DOCUMENT_TYPE")=="STOCK_DOCUMENT"&&d.L("ORIGIN_DOCUMENT_ID")==sid).Sum(d=>d.D("ORIGINAL_AMOUNT_VND"));
    var offset=checkout is null?0:offsets.Where(x=>x.L("CHECKOUT_ID")==checkout.L("ID")).Sum(x=>x.D("AMOUNT_VND"));
    var equation=new{documentId=sid,total=s.D("TOTAL_AMOUNT_VND"),postedCash=cash,originalDebt=debt,nonCashOffset=offset,difference=s.D("TOTAL_AMOUNT_VND")-cash-debt-offset,checkoutId=checkout?.L("ID")};
    receiptEquations.Add(equation);if(equation.difference!=0)receiptEquationExceptions.Add(equation);
}
var directLinkExceptions=new List<object>();
foreach(var table in new[]{"STOCK_DOCUMENT_PAYMENTS","PAYABLE_DEBT_PAYMENT_ALLOCATIONS","ORDER_PAYMENTS"})
{
    foreach(var g in T(table).Where(p=>p.L("CASH_VOUCHER_ID")>0&&(table!="ORDER_PAYMENTS"||!p.B("IS_VOIDED"))).GroupBy(p=>p.L("CASH_VOUCHER_ID")))
    {
        var v=vs.GetValueOrDefault(g.Key);var sum=g.Sum(p=>p.D(table=="ORDER_PAYMENTS"?"AMOUNT":"AMOUNT_VND"));
        if(v is null||sum!=v.D("AMOUNT_VND"))directLinkExceptions.Add(new{family=table,voucherId=g.Key,linkedAmount=sum,voucherAmount=v?.D("AMOUNT_VND"),rowIds=g.Select(p=>p.L("ID"))});
    }
}
var fundScope=new List<object>();var inactiveTargets=new List<object>();
foreach(var v in vs.Values.Where(Posted))
{
    foreach(var pair in new[]{("CASH_FUNDS","CASH_FUND_ID"),("BANK_ACCOUNTS","BANK_ACCOUNT_ID")})
    {
        var id=v.L(pair.Item2);if(id==0)continue;var target=T(pair.Item1).FirstOrDefault(t=>t.L("ID")==id);
        if(target is null||target.L("COMPANY_ID")!=v.L("COMPANY_ID")||target.L("BRANCH_ID")!=v.L("BRANCH_ID"))fundScope.Add(new{voucherId=v.L("ID"),kind=pair.Item1,targetId=id,amount=v.D("AMOUNT_VND")});
        else if(!target.B("IS_ACTIVE"))inactiveTargets.Add(new{voucherId=v.L("ID"),kind=pair.Item1,targetId=id,amount=v.D("AMOUNT_VND")});
    }
}
sections["R15_direct_sources"]=new{receiptEquations,receiptEquationExceptions,directLinkExceptions,fundScope,inactiveTargets};
var independentCashSql="SELECT COMPANY_ID,BRANCH_ID,COUNT(*) AS VOUCHERS,SUM(CASE VOUCHER_TYPE WHEN 'IN' THEN AMOUNT_VND ELSE -AMOUNT_VND END) AS NET FROM CASH_VOUCHERS WHERE STATUS_CODE='POSTED' GROUP BY COMPANY_ID,BRANCH_ID";
sqlLog.Add(independentCashSql+";");
sections["R16_independent_SQL"]=await c.QueryAsync(new CommandDefinition(independentCashSql,transaction:tx));
await tx.RollbackAsync();
var report=new{metadata,sections};
await File.WriteAllTextAsync(Path.Combine(output,"results.json"),JsonSerializer.Serialize(report,JsonOptions()));
await File.WriteAllTextAsync(Path.Combine(output,"selects.sql"),"-- Executed only inside Firebird READ ONLY SNAPSHOT transaction.\n"+string.Join("\n",sqlLog));
Console.WriteLine("READ_ONLY="+readOnly+" SCHEMA="+schema+" RESULTS="+Path.Combine(output,"results.json"));
foreach(var section in sections)Console.WriteLine(section.Key+" "+JsonSerializer.Serialize(section.Value));
static JsonSerializerOptions JsonOptions()=>new(){WriteIndented=true};
sealed class Row:Dictionary<string,object>
{
    public Row(IDictionary<string,object> data):base(data,StringComparer.OrdinalIgnoreCase){}
    public long L(string k)=>TryGetValue(k,out var x)&&x is not null and not DBNull?Convert.ToInt64(x,CultureInfo.InvariantCulture):0;
    public decimal D(string k)=>TryGetValue(k,out var x)&&x is not null and not DBNull?Convert.ToDecimal(x,CultureInfo.InvariantCulture):0;
    public string S(string k)=>TryGetValue(k,out var x)&&x is not null and not DBNull?Convert.ToString(x,CultureInfo.InvariantCulture)?.Trim()??"":"";
    public bool B(string k)=>TryGetValue(k,out var x)&&x is not null and not DBNull&&Convert.ToBoolean(x,CultureInfo.InvariantCulture);
}
