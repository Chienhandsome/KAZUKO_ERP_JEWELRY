using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.PriceBoard;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.PriceBoard;

if(args.Length!=2||args[0] is not ("start" or "verify")||!Path.GetFileName(args[1]).StartsWith("test89_",StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Use start|verify and a restored test89_*.fdb only; never DATA.FDB.");
var testPath=Path.GetFullPath(args[1]);
var root=Path.GetFullPath("artifacts/price-board-host-check");Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("KAZUKO_TV_SETTINGS_ROOT",Path.Combine(root,"settings"));
FirebirdDatabase.ApplyProfileForCurrentProcess(ConnectionProfileStore.Load(testPath) with {DatabasePath=testPath,UseEmbedded=false});
await using var db=FirebirdDatabase.CreateConnection();await db.OpenAsync();
var user=await db.QueryFirstAsync<long>("SELECT U.ID FROM USERS U JOIN ROLES R ON R.ID=U.ROLE_ID WHERE U.IS_ACTIVE=TRUE AND R.CODE='ADMIN' ORDER BY U.ID");
var branch=await db.QueryFirstAsync<long>("SELECT B.ID FROM BRANCHES B JOIN COMPANIES C ON C.ID=B.COMPANY_ID WHERE C.CODE='KAZUKO' ORDER BY B.ID");
var company=await db.QuerySingleAsync<long>("SELECT COMPANY_ID FROM BRANCHES WHERE ID=@branch",new{branch});
var session=new AuthenticatedSession(user,"host-test","Host Test","ADMIN",branch,"TEST","Test");
var app=Path.GetFullPath("src/Kazuko.App/bin/Debug/net8.0-windows/KAZUKO_ERP_JEWELRY.exe");
var factory=new PriceBoardTvHostClientFactory(app,Path.ChangeExtension(app,".dll"));
var client=await factory.ConnectAsync(session,company);
var status=await client.StatusAsync();
var manifestPath=Path.Combine(root,"manifest.json");
if(args[0]=="start")
{
    var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
    status=await client.ConfigureAsync(new(Enabled:true,AllowLan:true,Port:port,ShopName:"Host lifecycle test"));
    Check(!string.IsNullOrWhiteSpace(status.LocalUrl)&&status.ProcessId!=Environment.ProcessId,"display is hosted by an independent process");
    Check(status.Prices.All(p=>p.CompanyId==company),"host reads prices only for the configured company");
    using var http=new HttpClient(new HttpClientHandler{UseProxy=false});
    Check((await http.GetStringAsync(status.LocalUrl+"data")).Contains("Host lifecycle test"),"public price page is available");
    var price=status.Prices.First();
    File.WriteAllText(manifestPath,JsonSerializer.Serialize(new Manifest(status.ProcessId,status.LocalUrl,port,price.Id,price.Name,price.BuyPricePerChi+123m)));
    Console.WriteLine("START PHASE COMPLETE. This controller process now exits; host must remain alive.");
    return;
}
var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))!;
try
{
    Check(status.ProcessId==manifest.ProcessId,"host survives after the first controller process exits");
    Check(status.LocalUrl==manifest.Url,"reopening ERP reuses the host and keeps the LAN address/token");
    await db.ExecuteAsync("UPDATE GOLD_PRICES SET BUY_PRICE_PER_CHI=@buy, UPDATED_AT=CURRENT_TIMESTAMP WHERE ID=@id",new{buy=manifest.ExpectedBuy,id=manifest.PriceId});
    using var http=new HttpClient(new HttpClientHandler{UseProxy=false});
    await Until(async()=>
    {
        using var json=JsonDocument.Parse(await http.GetStringAsync(manifest.Url+"data"));
        return json.RootElement.GetProperty("rows").EnumerateArray().Any(row=>row.GetProperty("name").GetString()==manifest.PriceName&&
            row.GetProperty("buy").GetString()==manifest.ExpectedBuy.ToString("N0",CultureInfo.GetCultureInfo("vi-VN")));
    },TimeSpan.FromSeconds(12));
    Check(true,"price changes are loaded from DB after the original controller is gone");
    var scope=PriceBoardHostProtocol.Scope(FirebirdDatabase.Profile,branch);
    await using(var pipe=new NamedPipeClientStream(".",PriceBoardHostProtocol.PipeName(scope),PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly))
    {
        await pipe.ConnectAsync(2000);
        await PriceBoardHostProtocol.WriteAsync(pipe,new PriceBoardHostRequest("configure",session,company+999,new()),default);
        var rejected=await PriceBoardHostProtocol.ReadAsync<PriceBoardHostReply>(pipe,default);
        Check(!string.IsNullOrEmpty(rejected.Error),"a request cannot switch the running host to another company");
    }
    Check((await client.StatusAsync()).ProcessId==manifest.ProcessId,"rejected IPC leaves the original host running");
    await db.ExecuteAsync("UPDATE USERS SET IS_ACTIVE=FALSE WHERE ID=@user",new{user});
    try
    {
        await Until(async()=>
        {
            using var json=JsonDocument.Parse(await http.GetStringAsync(manifest.Url+"data"));
            return !json.RootElement.GetProperty("active").GetBoolean()&&json.RootElement.GetProperty("rows").GetArrayLength()==0;
        },TimeSpan.FromSeconds(22));
        Check(true,"disabling the publishing account hides public prices without ERP running");
        try{await client.ConfigureAsync(status.Settings);throw new Exception("Revoked account was allowed to configure host.");}
        catch(InvalidOperationException){Check(true,"revoked account cannot change the detached host through IPC");}
    }
    finally{await db.ExecuteAsync("UPDATE USERS SET IS_ACTIVE=TRUE WHERE ID=@user",new{user});}
    await Until(async()=>
    {
        using var json=JsonDocument.Parse(await http.GetStringAsync(manifest.Url+"data"));
        return json.RootElement.GetProperty("active").GetBoolean();
    },TimeSpan.FromSeconds(22));
    Check(true,"price publication recovers after the account is restored");
}
finally
{
    await client.StopAsync();
    await Until(()=>Task.FromResult(!IsRunning(manifest.ProcessId)),TimeSpan.FromSeconds(8));
}
var replacement=new TcpListener(IPAddress.Any,manifest.Port);replacement.Start();replacement.Stop();
Check(true,"stopping the host exits its process and releases the listening port");
Console.WriteLine("ALL DETACHED TV HOST CHECKS PASSED; only the restored test DB was modified.");
static bool IsRunning(int id){try{using var p=Process.GetProcessById(id);return !p.HasExited;}catch(ArgumentException){return false;}}
static void Check(bool value,string label){if(!value)throw new Exception("FAILED: "+label);Console.WriteLine("PASS: "+label);}
static async Task Until(Func<Task<bool>> check,TimeSpan timeout){var watch=Stopwatch.StartNew();while(watch.Elapsed<timeout){if(await check())return;await Task.Delay(250);}throw new TimeoutException("Condition did not complete.");}
sealed record Manifest(int ProcessId,string Url,int Port,long PriceId,string PriceName,decimal ExpectedBuy);
