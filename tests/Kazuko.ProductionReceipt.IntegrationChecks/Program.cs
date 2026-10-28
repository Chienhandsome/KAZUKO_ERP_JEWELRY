using System.Diagnostics;
using System.Security.Cryptography;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Production;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Production;

var repo=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
if(args.Length==2&&args[0]=="--prepare")
{
    var target=Path.GetFullPath(args[1]);
    if(!Path.GetFileName(target).StartsWith("test91_",StringComparison.OrdinalIgnoreCase)||File.Exists(target))throw new ArgumentException("Use a NEW test91_*.fdb.");
    var profile=FirebirdDatabase.Profile;Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    FirebirdDatabase.ApplyProfileForCurrentProcess(profile with{UseEmbedded=false});
    await using(var check=FirebirdDatabase.CreateConnection()){await check.OpenAsync();Console.WriteLine("Live schema: "+await check.ExecuteScalarAsync<int>("SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS"));}
    var backup=target+".source.fbk";
    await Tool("gbak.exe",["-b","-g",$"{profile.Host}:{profile.DatabasePath}",backup],profile);
    await Tool("gbak.exe",["-c",backup,$"{profile.Host}:{target}"],profile);
    var migration=Path.Combine(repo,"database/migrations/0091__production_receipt_reliability.sql");
    var script=target+".migration.sql";
    var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(migration)));
    await File.WriteAllTextAsync(script,await File.ReadAllTextAsync(migration)+$"\nINSERT INTO SCHEMA_MIGRATIONS(VERSION_NO,NAME,CHECKSUM) VALUES(91,'production_receipt_reliability','{hash}');\nCOMMIT;\n");
    // Use the official migration pipeline for ledger column compatibility and backup/rollback.
    await PowerShellMigration(target,profile);
    Console.WriteLine("Prepared isolated DB: "+target);return;
}
if(args.Length==2&&args[0]=="--migrate-test"&&Path.GetFileName(args[1]).StartsWith("test91_",StringComparison.OrdinalIgnoreCase))
{await PowerShellMigration(Path.GetFullPath(args[1]),FirebirdDatabase.Profile);return;}
if(args.Length==1&&args[0]=="--migrate-live")
{
    var profile=FirebirdDatabase.Profile;
    var backup=Path.Combine(repo,"../artifacts/production-receipt","before91_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".fbk");Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
    await Tool("gbak.exe",["-b","-g",$"{profile.Host}:{profile.DatabasePath}",backup],profile);
    await PowerShellMigration(profile.DatabasePath,profile);Console.WriteLine("Backup: "+backup);return;
}
if(args.Length!=1||!Path.GetFileName(args[0]).StartsWith("test91_",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("ONLY restored test91_*.fdb; never DATA.FDB.");
var path=Path.GetFullPath(args[0]);var testProfile=ConnectionProfileStore.Load(path) with{DatabasePath=path,UseEmbedded=false};FirebirdDatabase.ApplyProfileForCurrentProcess(testProfile);
await using var c=FirebirdDatabase.CreateConnection();await c.OpenAsync();
var branch=await c.QueryFirstAsync<long>("SELECT ID FROM BRANCHES WHERE IS_ACTIVE=TRUE ORDER BY ID");
var user=await c.QueryFirstAsync<long>("SELECT ID FROM USERS WHERE IS_ACTIVE=TRUE ORDER BY ID");
var employee=await c.QueryFirstAsync<long>("SELECT ID FROM HR_EMPLOYEES WHERE IS_ACTIVE=TRUE ORDER BY ID");
var raw=await c.QueryFirstAsync<long>("SELECT ID FROM LOCATIONS WHERE BRANCH_ID=@branch AND INVENTORY_SCOPE='RAW_MATERIAL' AND IS_ACTIVE=TRUE",new{branch});
var finished=await c.QueryFirstAsync<long>("SELECT ID FROM LOCATIONS WHERE BRANCH_ID=@branch AND INVENTORY_SCOPE='FINISHED_GOODS' AND IS_ACTIVE=TRUE",new{branch});
var gold=await c.QueryFirstAsync<(long Id,string Code,string Name,long MaterialId)>("""
    SELECT G.ID,G.CODE,G.NAME,M.ID FROM GOLD_TYPES G JOIN MATERIALS M ON M.GOLD_TYPE_ID=G.ID
    JOIN STOCK_BALANCES B ON B.MATERIAL_ID=M.ID
    JOIN WAREHOUSE_GOLD_PRICES P ON P.GOLD_TYPE_ID=G.ID
    WHERE G.IS_ACTIVE=TRUE AND B.LOCATION_ID=@raw AND B.QUANTITY>3 AND P.ISSUE_PRICE_PER_CHI>0 ORDER BY G.ID
    """,new{raw});
var session=new AuthenticatedSession(user,"test","Receipt test","ADMIN",branch,"TEST","Test");
var service=new FirebirdProductionApplicationService(new LocalProductionAttachmentStore());var query=new FirebirdProductionQueryRepository();
var id=await CreateOrder();var detail=(await query.GetOrderDetailAsync(id,branch))!;
var item=new ManualFinishedProductItem("TEMP","TEMP","Nhẫn test",null,gold.Code,gold.Code,"Chỉ",1,.1m,.9m,1000000,1200000,100000,150000,100000,"Nhà cung cấp","M1",null,"Ghi chú món",detail.Lines[0].Id);
var request=new PostManualFinishedReceiptRequest(DateTime.Today,finished,id,[item,item with{ProductCode="TEMP2"}],"Ghi chú phiếu",Guid.NewGuid(),[new(gold.Code,.2m,employee,WorkerGoldDebtWorkerTypes.Employee)]);
var docs=await Count("STOCK_DOCUMENTS");var products=await Count("FINISHED_PRODUCTS");var debts=await Count("WORKER_GOLD_DEBTS");
await Reject(()=>service.PostManualFinishedReceiptAsync(request with{Items=[item]},session),"missing piece rejected");
await Reject(()=>service.PostManualFinishedReceiptAsync(request with{Items=[item,item with{OrderLineId=long.MaxValue}]},session),"foreign planned line rejected");
await Reject(()=>service.PostManualFinishedReceiptAsync(request with{GoldSettlements=null},session),"unconfirmed settlement rejected");
Check(await Count("STOCK_DOCUMENTS")==docs&&await Count("FINISHED_PRODUCTS")==products,"validation failures roll back all stock");
var trigger="T_RECEIPT_FAIL";
await c.ExecuteAsync("CREATE EXCEPTION E_RECEIPT_FAIL 'Injected audit error'");
await c.ExecuteAsync($"CREATE TRIGGER {trigger} FOR AUDIT_LOG ACTIVE BEFORE INSERT POSITION 0 AS BEGIN EXCEPTION E_RECEIPT_FAIL; END");
try{await Reject(()=>service.PostManualFinishedReceiptAsync(request,session),"audit failure rejects posting");}
finally{await c.ExecuteAsync($"DROP TRIGGER {trigger}");await c.ExecuteAsync("DROP EXCEPTION E_RECEIPT_FAIL");}
Check(await Count("STOCK_DOCUMENTS")==docs&&await Count("FINISHED_PRODUCTS")==products&&await Count("WORKER_GOLD_DEBTS")==debts,"audit rollback leaves no stock/debt");
var result=await service.PostManualFinishedReceiptAsync(request,session);var retry=await service.PostManualFinishedReceiptAsync(request,session);
Check(result.DocumentId==retry.DocumentId,"retry returns original document");
Check(result.PostedRequest!.Items.Select(i=>i.ProductCode).Distinct().Count()==2,"official unique serial per piece");
await Reject(()=>service.PostManualFinishedReceiptAsync(request with{Notes="Changed"},session),"same request ID with changed body rejected");
await Reject(()=>service.PostManualFinishedReceiptAsync(request with{RequestId=Guid.NewGuid()},session),"second request cannot re-receive order");
var loaded=await new FirebirdFinishedReceiptDocuments().GetByOrderAsync(id,session);
Check(loaded!.PostedRequest!.Items[0].SupplierName=="Nhà cung cấp"&&loaded.PostedRequest.Items[0].SalePrice==1200000,"stored payload preserves all entry fields");
Check(await new FirebirdFinishedReceiptDocuments().GetByOrderAsync(id,session with{BranchId=long.MaxValue}) is null,"cross branch read rejected");
Check(await Count("STOCK_DOCUMENTS")==docs+1&&await Count("FINISHED_PRODUCTS")==products+2,"exactly one receipt and two products");
Check(await Count("WORKER_GOLD_DEBTS")==debts+1,"exactly one gold debt posted atomically");
Check(await c.ExecuteScalarAsync<decimal>("SELECT WEIGHT_CHI FROM WORKER_GOLD_DEBTS WHERE PRODUCTION_ORDER_ID=@id",new{id})==.5m,"gold debt recomputed from database issue and received items");
var second=await CreateOrder();var secondDetail=(await query.GetOrderDetailAsync(second,branch))!;
var next=request with{ProductionOrderId=second,RequestId=Guid.NewGuid(),Items=request.Items.Select(i=>i with{OrderLineId=secondDetail.Lines[0].Id}).ToArray()};
var attempts=await Task.WhenAll(TryPost(next),TryPost(next with{RequestId=Guid.NewGuid()}));
Check(attempts.Count(x=>x)==1,"two sessions racing one order allow exactly one posting");
Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM STOCK_DOCUMENTS WHERE PRODUCTION_ORDER_ID=@id AND DOCUMENT_TYPE='PRODUCTION_FINISHED_RECEIPT'",new{id=second})==1,"race leaves exactly one finished receipt");
Console.WriteLine("ALL PRODUCTION RECEIPT CHECKS PASSED");
async Task<bool> TryPost(PostManualFinishedReceiptRequest value){try{await service.PostManualFinishedReceiptAsync(value,session);return true;}catch{return false;}}
async Task<long> CreateOrder()
{
    var draft=new ProductionOrderDraft(null,null,ProductionOrderSources.Stock,null,ProductionExecutionTypes.InHouse,null,DateTime.Today,null,raw,finished,null,null,[employee],
        [new(null,1,"TEST","Nhẫn test",gold.Code,gold.Name,2,100000,1,1000000,0,null,null)],[]);
    var saved=await service.SaveDraftAsync(draft,session);
    var issued=await service.PostMaterialIssueAsync(new(saved.Id,saved.RowVersion,DateTime.Now,null,null,null,[new(gold.MaterialId,2.5m,null)]),session);
    await service.CompleteAsync(saved.Id,issued.RowVersion,session);
    return saved.Id;
}
async Task<long> Count(string table)=>await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM "+table);
static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
static async Task Reject(Func<Task> action,string label){try{await action();}catch(Exception){Console.WriteLine("PASS: "+label);return;}throw new Exception("Accepted: "+label);}
async Task PowerShellMigration(string database,ConnectionProfile profile)
{
    var script=Path.Combine(repo,"database/tools/Invoke-KazukoMigration.ps1");
    var start=new ProcessStartInfo("pwsh.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-MigrationPath",Path.Combine(repo,"database/migrations/0091__production_receipt_reliability.sql"),"-Database",$"{profile.Host}:{database}","-IsqlPath",Path.Combine(FirebirdRuntime.HomeDirectory,"isql.exe"),"-GbakPath",Path.Combine(FirebirdRuntime.HomeDirectory,"gbak.exe")})start.ArgumentList.Add(arg);
    start.Environment["ISC_USER"]=profile.UserName;start.Environment["KAZUKO_FIREBIRD_PASSWORD"]=profile.Password;
    using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
    Console.WriteLine(await output);if(process.ExitCode!=0)throw new Exception("Migration failed: "+await error);
}
static async Task Tool(string tool,string[] arguments,ConnectionProfile profile)
{
    var start=new ProcessStartInfo(Path.Combine(FirebirdRuntime.HomeDirectory,tool)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
    foreach(var arg in arguments)start.ArgumentList.Add(arg);start.Environment["ISC_USER"]=profile.UserName;start.Environment["ISC_PASSWORD"]=profile.Password;
    using var process=Process.Start(start)!;var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();if(process.ExitCode!=0)throw new Exception("Firebird tool failed: "+await error);
}
