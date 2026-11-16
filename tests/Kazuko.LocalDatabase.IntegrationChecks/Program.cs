using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Infrastructure.Authentication;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.UserManagement;
using Kazuko.Core.Authentication;

var root=Path.GetFullPath("artifacts/local-database/test89_"+Guid.NewGuid().ToString("N")+"/Cửa hàng mới");
Console.WriteLine("Active Windows code page: "+Native.GetACP());
var runtime=Path.GetFullPath("src/Kazuko.App/bin/Debug/net8.0-windows/firebird");
Environment.SetEnvironmentVariable("KAZUKO_PORTABLE_MODE","0");
Environment.SetEnvironmentVariable("KAZUKO_FIREBIRD_HOME",runtime);
var first=new LocalDatabaseBootstrapper(root,runtime,"test89_fresh.fdb");
var second=new LocalDatabaseBootstrapper(root,runtime,"test89_fresh.fdb");
var results=await Task.WhenAll(first.EnsureAsync(),second.EnsureAsync());
Check(results.Count(r=>r.Created)==1,"concurrent launches create exactly one database");
var result=results.First(r=>r.Created);
Check(result.InitialAdminPassword==string.Empty,"administrator default password is empty");
Check(results.All(r=>r.Profile==result.Profile),"concurrent launches reuse the same database and engine credential");
var saved=ConnectionProfileStore.Load(first.DatabasePath,root);
Check(saved==result.Profile,"protected engine credential reloads without environment configuration");
var profileJson=File.ReadAllText(Path.Combine(root,"connection.json"));
Check(!profileJson.Contains(result.Profile.Password),"profile contains no plaintext engine credential");
foreach(var key in new[]{"local-database.key","initial-admin.key"})
{
    var bytes=Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,key)));
    Check(!bytes.Contains(result.Profile.Password),key+" is protected");
}
await using(var db=new FbConnection(FirebirdDatabase.BuildConnectionString(saved) + ";Pooling=false"))
{
    await db.OpenAsync();
    var allowed=new HashSet<string>{"COMPANIES","BRANCHES","LOCATIONS","ROLES","PERMISSIONS","ROLE_PERMISSIONS","USER_GROUPS","GROUP_PERMISSIONS",
        "USERS","CASH_FUNDS","CUSTOMER_GROUPS","CUSTOMERS","SUPPLIER_GROUPS","FINISHED_PRODUCT_GROUPS","GOLD_TYPES","GOLD_PRICES","WAREHOUSE_GOLD_PRICES","MATERIALS","HR_WORK_SHIFTS","VOUCHER_CATEGORIES","SCHEMA_MIGRATIONS"};
    foreach(var table in await db.QueryAsync<string>("SELECT TRIM(RDB$RELATION_NAME) FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG,0)=0 AND RDB$VIEW_BLR IS NULL"))
        if(!allowed.Contains(table))Check(await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM "+table)==0,"empty business table "+table);
    Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM USERS")==1,"no source users are copied");
    Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CUSTOMERS")==1,"only the walk-in customer is seeded");
    Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CUSTOMERS WHERE IS_WALK_IN=TRUE")==1,"walk-in customer is usable by checkout");
    Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM MATERIALS WHERE GOLD_TYPE_ID IS NOT NULL")==4,"gold catalog has linked raw materials");
    Check(await db.ExecuteScalarAsync<decimal>("SELECT SUM(BUY_PRICE_PER_CHI+SELL_PRICE_PER_CHI) FROM GOLD_PRICES")==0,"no developer prices are copied");
    Check(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SCHEMA_MIGRATIONS")==93,"clean database has the complete v93 migration ledger");
    await db.ExecuteAsync("UPDATE COMPANIES SET NAME='Cửa hàng kiểm thử' WHERE CODE='KAZUKO'");
}
var again=await first.EnsureAsync();
Check(!again.Created,"reopening does not recreate the database");
File.Delete(Path.Combine(root,"connection.json")); // Simulate interruption after publishing the DB, before publishing its profile.
var recovered=await first.EnsureAsync();
Check(!recovered.Created&&recovered.Profile==saved,"interrupted profile publication recovers without recreating DB or credentials");
await using(var db=new FbConnection(FirebirdDatabase.BuildConnectionString(saved)+";Pooling=false"))
{
    await db.OpenAsync();
    Check(await db.ExecuteScalarAsync<string>("SELECT NAME FROM COMPANIES WHERE CODE='KAZUKO'")=="Cửa hàng kiểm thử","existing changes survive reopening");
}
var newPath = Path.Combine(root, "test89_login_new.fdb");
var loginDatabase = await first.CreateNewAsync(newPath);
Check(loginDatabase.DatabasePath == newPath && File.Exists(newPath), "login creates a separate database at the selected path");
Check(ConnectionProfileStore.Load(first.DatabasePath, root) == saved, "creation alone preserves the selected profile");
try
{
    await first.CreateNewAsync(newPath);
    throw new Exception("Existing database was accepted by CreateNewAsync.");
}
catch (InvalidOperationException) { }
await new LoginDatabaseSelector().ValidateAsync(newPath, loginDatabase);

var backup=Path.Combine(root,"test89_clean.fbk");
var restored=Path.Combine(root,"test89_restored.fdb");
await RunNative("-backup_database",first.DatabasePath,backup);
await RunNative("-create_database",backup,restored);
await using(var db=new FbConnection(FirebirdDatabase.BuildConnectionString(saved with{DatabasePath=restored})+";Pooling=false"))
{
    await db.OpenAsync();
    Check(await db.ExecuteScalarAsync<int>("SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS")==93,"new DB passes backup/restore with its schema intact");
}
var previous=FirebirdDatabase.Profile;
// Login file selection uses an existing isolated restore, never the operational DB/profile.
var selector = new LoginDatabaseSelector();
var selected = await selector.ValidateAsync(restored, saved with { Host = "remote-server", UseEmbedded = false });
Check(selected.UseEmbedded && selected.Host == "localhost" && selected.DatabasePath == restored,
    "selecting a local file does not inherit server mode");
var selectionRoot = Path.Combine(root, "selected-profile");
ConnectionProfileStore.Save(selected, selectionRoot);
var reloadedSelection = ConnectionProfileStore.Load("unused.fdb", selectionRoot);
Check(reloadedSelection == selected, "selected DB and protected engine credential survive reopening");
var selectionJson = File.ReadAllText(Path.Combine(selectionRoot, "connection.json"));
Check(!selectionJson.Contains(selected.Password), "selected DB profile does not contain plaintext credentials");
try { await selector.ValidateAsync(Path.Combine(root, "test89_missing.fdb"), saved); throw new Exception("Missing file accepted"); }
catch (InvalidOperationException) { Check(true, "missing DB is rejected without creating data"); }
await using (var db = new FbConnection(FirebirdDatabase.BuildConnectionString(selected) + ";Pooling=false"))
{
    await db.OpenAsync();
    await db.ExecuteAsync("DELETE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO=93");
}
try { await selector.ValidateAsync(restored, saved); throw new Exception("Incompatible schema accepted"); }
catch (InvalidOperationException) { Check(true, "incompatible schema is rejected without migrating it"); }
Check(File.ReadAllText(Path.Combine(selectionRoot,"connection.json")) == selectionJson,
    "failed validation preserves the saved selection");
var credentialName = JsonDocument.Parse(selectionJson).RootElement.GetProperty("CredentialFile").GetString()!;
File.Delete(Path.Combine(selectionRoot, credentialName));
try { ConnectionProfileStore.Load("unused.fdb", selectionRoot); throw new Exception("Missing credential accepted"); }
catch (InvalidOperationException) { Check(true, "missing selected credential does not fall back to another DB password"); }
try
{
    FirebirdDatabase.ApplyProfileForCurrentProcess(saved);
    var auth=new FirebirdAuthenticationService();
    var branch=(await auth.GetBranchesAsync()).Single();
    var login=await auth.SignInAsync("admin",result.InitialAdminPassword!,branch.Code);
    Check(login.IsSuccess,"generated administrator can sign in");
    var session=new CurrentSessionAccessor();session.Set(login.Session!);
    var rights=new FirebirdAuthorizationService(session);await rights.RefreshAsync();
    Check(rights.Current.IsSystemAdmin,"generated administrator has system access");
}
finally {FirebirdDatabase.ApplyProfileForCurrentProcess(previous);}
first.AcknowledgeAdminCredentials();
Check((await first.EnsureAsync()).InitialAdminPassword is null,"initial password notice ends after acknowledgement");

// Upgrade only the old generated default, never a password chosen/used by the owner.
const string oldDefault="Legacy-initial-123!";
await SetTestPassword(oldDefault,false);
LocalDatabaseSecrets.Create(root,"initial-admin.key",oldDefault,overwrite:true);
Check((await first.EnsureAsync()).InitialAdminPassword=="","unused generated default upgrades to empty");
Check(await PasswordMatches(""),"upgraded administrator accepts an empty password");
await SetTestPassword("Owner-selected-456!",true);
LocalDatabaseSecrets.Create(root,"initial-admin.key",oldDefault,overwrite:true);
await first.EnsureAsync();
Check(await PasswordMatches("Owner-selected-456!"),"administrator who already signed in keeps their password");
await SetTestPassword("Owner-selected-789!",false);
await first.EnsureAsync();
Check(await PasswordMatches("Owner-selected-789!"),"changed password is preserved even before first sign-in");

var unowned=new LocalDatabaseBootstrapper(Path.Combine(root,"unowned"),runtime,"test89_existing.fdb");
Directory.CreateDirectory(Path.GetDirectoryName(unowned.DatabasePath)!);
File.WriteAllText(unowned.DatabasePath,"existing user data");
try {await unowned.EnsureAsync();throw new Exception("Existing database was replaced.");}
catch(InvalidOperationException){Check(File.ReadAllText(unowned.DatabasePath)=="existing user data","unowned existing database is never replaced");}
var configured=Path.Combine(root,"configured");Directory.CreateDirectory(configured);
var missing=Path.Combine(configured,"test89_missing.fdb");
File.WriteAllText(Path.Combine(configured,"connection.json"),JsonSerializer.Serialize(new{Host="existing-server",Port=3050,DatabasePath=missing,UserName="SYSDBA",UseEmbedded=false}));
var existing=await new LocalDatabaseBootstrapper(configured,runtime,"test89_never.fdb").EnsureAsync();
Check(!existing.Created&&!File.Exists(missing)&&existing.Profile.Host=="existing-server","existing configuration is preserved even if its DB is unavailable");
var failedRoot=Path.Combine(root,"failed-start");
var failed=false;
try {await new LocalDatabaseBootstrapper(failedRoot,Path.Combine(root,"missing-runtime"),"test89_retry.fdb").EnsureAsync();}
catch(FileNotFoundException) { failed=true; }
Check(failed&&!File.Exists(Path.Combine(failedRoot,"connection.json")),"failed creation never publishes a profile");
var retry=await new LocalDatabaseBootstrapper(failedRoot,runtime,"test89_retry.fdb").EnsureAsync();
Check(retry.Created,"creation retries successfully after an interrupted/failed attempt");
var pendingRoot=Path.Combine(root,"old-pending-key");
LocalDatabaseSecrets.Create(pendingRoot,"initial-admin.key",oldDefault);
var pending=await new LocalDatabaseBootstrapper(pendingRoot,runtime,"test89_pending.fdb").EnsureAsync();
Check(pending.InitialAdminPassword=="","old interrupted creation uses the new empty default");
Console.WriteLine("ALL LOCAL FIRST-RUN CHECKS PASSED. No live DB/profile was changed.");
static void Check(bool value,string message){if(!value)throw new Exception("FAILED: "+message);Console.WriteLine("PASS: "+message);}
async Task RunNative(params string[] arguments)
{
    await new GbakRunner().RunAsync(runtime,saved,arguments,default);
}
async Task SetTestPassword(string password,bool used)
{
    var salt=RandomNumberGenerator.GetBytes(16);
    var hash=Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32));
    await using var db=new FbConnection(FirebirdDatabase.BuildConnectionString(saved)+";Pooling=false");await db.OpenAsync();
    await db.ExecuteAsync("UPDATE USERS SET PASSWORD_HASH=@Hash,PASSWORD_SALT=@Salt,PASSWORD_ITERATIONS=210000,LAST_LOGIN_AT=@LastLogin WHERE USERNAME='admin'",
        new{Hash=hash,Salt=Convert.ToHexString(salt),LastLogin=used?(DateTime?)DateTime.Now:null});
}
async Task<bool> PasswordMatches(string password)
{
    await using var db=new FbConnection(FirebirdDatabase.BuildConnectionString(saved)+";Pooling=false");await db.OpenAsync();
    var row=await db.QuerySingleAsync("SELECT PASSWORD_HASH AS HASH,PASSWORD_SALT AS SALT FROM USERS WHERE USERNAME='admin'");
    var hash=Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromHexString((string)row.SALT),210000,HashAlgorithmName.SHA256,32));
    return hash==(string)row.HASH;
}
internal static class Native
{
    [DllImport("kernel32.dll")] internal static extern uint GetACP();
}
