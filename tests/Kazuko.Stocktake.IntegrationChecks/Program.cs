using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

if(args.Length!=1 || !Path.GetFileName(args[0]).StartsWith("test90_",StringComparison.OrdinalIgnoreCase))
 throw new ArgumentException("Use ONLY a restored test90_*.fdb, never DATA.FDB.");
var profile=ConnectionProfileStore.Load(args[0]) with {DatabasePath=Path.GetFullPath(args[0]),UseEmbedded=false};
FirebirdDatabase.ApplyProfileForCurrentProcess(profile);
await using var c=FirebirdDatabase.CreateConnection();await c.OpenAsync();
var branch=await c.QueryFirstAsync<long>("SELECT B.ID FROM BRANCHES B JOIN COMPANIES C ON C.ID=B.COMPANY_ID WHERE B.IS_ACTIVE=TRUE AND C.CODE='KAZUKO' ORDER BY B.ID");
var user=await c.QueryFirstAsync<long>("SELECT ID FROM USERS WHERE IS_ACTIVE=TRUE ORDER BY ID");
var session=new AuthenticatedSession(user,"test","Stocktake test","ADMIN",branch,"TEST","Test");
var service=new FirebirdStocktakeService();
var locations=await service.GetLocationsAsync(session);
var raw=locations.First(x=>x.InventoryScope==InventoryScopes.RawMaterial);
var productLocation=locations.First(x=>x.InventoryScope==InventoryScopes.FinishedGoods);
var movements=await Count("STOCK_MOVEMENTS");var fpMoves=await Count("FINISHED_PRODUCT_MOVEMENTS");var docs=await Count("STOCK_DOCUMENTS");var vouchers=await Count("CASH_VOUCHERS");
var request=new StocktakeCreateRequest(Guid.NewGuid(),DateTime.Today,raw.Id,"Integration count");
var draft=await service.CreateAsync(request,session);
Check((await service.CreateAsync(request,session)).Id==draft.Id,"create retry is idempotent");
Check(draft.Lines.All(x=>x.Kind=="MATERIAL"&&x.CountedQuantity is null),"material snapshot begins uncounted");
await Reject(()=>service.CloseAsync(draft.Id,draft.Revision,session),"unentered material count blocks close");
draft=await Fill(draft);
var reloaded=await service.GetAsync(draft.Id,session);Check(reloaded.Lines.All(x=>x.CountedQuantity==x.SystemQuantity),"saved draft reloads exact counts");
await Reject(()=>service.SaveAsync(new(draft.Id,1,"stale",[]),session),"stale revision rejected");
await Reject(()=>service.GetAsync(draft.Id,session with{BranchId=long.MaxValue}),"cross-branch read rejected");
var balance=await c.QueryFirstAsync<(long MaterialId,decimal Quantity)>("SELECT MATERIAL_ID,QUANTITY FROM STOCK_BALANCES WHERE LOCATION_ID=@Id ORDER BY MATERIAL_ID",raw);
await c.ExecuteAsync("UPDATE STOCK_BALANCES SET QUANTITY=QUANTITY+1 WHERE MATERIAL_ID=@MaterialId AND LOCATION_ID=@Id",new{balance.MaterialId,raw.Id});
await c.ExecuteAsync("UPDATE STOCK_BALANCES SET QUANTITY=QUANTITY-1 WHERE MATERIAL_ID=@MaterialId AND LOCATION_ID=@Id",new{balance.MaterialId,raw.Id});
await Reject(()=>service.CloseAsync(draft.Id,draft.Revision,session),"inventory round-trip still invalidates baseline");
draft=await service.ReviewAsync(draft.Id,draft.Revision,session);
Check(draft.Lines.Single(x=>x.ItemId==balance.MaterialId).NeedsRecount,"changed material marked for explicit recount");
Check(draft.Lines.Where(x=>x.ItemId!=balance.MaterialId).All(x=>!x.NeedsRecount),"unchanged lines preserve previous counts");
await Reject(()=>service.CloseAsync(draft.Id,draft.Revision,session),"review alone cannot bypass recount");
draft=await Fill(draft);draft=await service.CloseAsync(draft.Id,draft.Revision,session);
Check(draft.Status=="COUNTED","material count closes without posting stock");
await Reject(()=>Fill(draft),"closed counts cannot be edited");

var serial=await service.CreateAsync(new(Guid.NewGuid(),DateTime.Today,productLocation.Id,"Serial count"),session);
var sample=serial.Lines.FirstOrDefault();
if(sample is null)throw new Exception("Integration fixture requires at least one received serial in finished warehouse.");
serial=await service.ScanAsync(serial.Id,serial.Revision,sample.Code,session);
serial=await service.ScanAsync(serial.Id,serial.Revision,sample.Code,session);
Check(serial.Lines.Single(x=>x.ItemKey==sample.ItemKey).CountedQuantity==1,"duplicate scan remains one item");
var unknown="UNKNOWN-"+Guid.NewGuid().ToString("N");
serial=await service.ScanAsync(serial.Id,serial.Revision,unknown,session);
Check(serial.Lines.Single(x=>x.Code==unknown).Kind=="UNKNOWN","unknown scan kept for investigation without creating product");
var outside=await c.QueryFirstOrDefaultAsync<string>("SELECT CODE FROM FINISHED_PRODUCTS WHERE BRANCH_ID=@branch AND (LOCATION_ID<>@Id OR LOCATION_ID IS NULL) ORDER BY ID",new{branch,productLocation.Id});
if(outside is not null)
{
 serial=await service.ScanAsync(serial.Id,serial.Revision,outside,session);
 Check(serial.Lines.Single(x=>x.Code==outside).SystemQuantity==0,"serial outside expected stock recorded separately");
}
await c.ExecuteAsync("UPDATE FINISHED_PRODUCTS SET ROW_VERSION=ROW_VERSION+1 WHERE ID=@ItemId",sample);
await Reject(()=>service.CloseAsync(serial.Id,serial.Revision,session),"serial change blocks stale close");
serial=await service.ReviewAsync(serial.Id,serial.Revision,session);
Check(serial.Lines.Single(x=>x.ItemKey==sample.ItemKey).NeedsRecount,"serial reset requires recount");
await Reject(()=>service.CloseAsync(serial.Id,serial.Revision,session),"serial needs explicit confirmation after review");
serial=await service.ScanAsync(serial.Id,serial.Revision,sample.Code,session);
serial=await service.CloseAsync(serial.Id,serial.Revision,session);
Check(serial.Status=="COUNTED","serial count closes with missing and unexplained findings preserved");

var auditDraft=await service.CreateAsync(new(Guid.NewGuid(),DateTime.Today,raw.Id,"Audit rollback"),session);
var tag=Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();var trigger="T_CT_"+tag;var exception="E_CT_"+tag;
await c.ExecuteAsync($"CREATE EXCEPTION {exception} 'Injected audit failure'");
await c.ExecuteAsync($"CREATE TRIGGER {trigger} FOR AUDIT_LOG ACTIVE BEFORE INSERT POSITION 0 AS BEGIN EXCEPTION {exception}; END");
try{await Reject(()=>Fill(auditDraft),"audit failure rejects count save");}
finally{await c.ExecuteAsync($"DROP TRIGGER {trigger}");await c.ExecuteAsync($"DROP EXCEPTION {exception}");}
Check((await service.GetAsync(auditDraft.Id,session)).Revision==auditDraft.Revision,"failed audit rolls back revision and lines");
var saves=await Task.WhenAll(Attempt(()=>Fill(auditDraft)),Attempt(()=>Fill(auditDraft)));
Check(saves.Count(x=>x)==1,"concurrent saves allow exactly one writer");
Check(await Count("STOCK_MOVEMENTS")==movements&&await Count("FINISHED_PRODUCT_MOVEMENTS")==fpMoves&&await Count("STOCK_DOCUMENTS")==docs&&await Count("CASH_VOUCHERS")==vouchers,"counting created no inventory movement, stock document or voucher");
Console.WriteLine("ALL STOCKTAKE INTEGRATION CHECKS PASSED. Only restored test90 database was written.");
Task<int> Count(string table)=>c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");
Task<StocktakeDetail> Fill(StocktakeDetail d)=>service.SaveAsync(new(d.Id,d.Revision,d.Notes,d.Lines.Select(x=>new StocktakeCountEntry(x.Id,x.SystemQuantity,x.Notes,true)).ToArray()),session);
static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
static async Task Reject(Func<Task> action,string label){try{await action();}catch(InvalidOperationException){Console.WriteLine("PASS: "+label);return;}throw new Exception("Expected rejection: "+label);}
static async Task<bool> Attempt(Func<Task> action){try{await action();return true;}catch{return false;}}
