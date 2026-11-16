using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Isql;
using Kazuko.Core.UserManagement;

namespace Kazuko.Infrastructure.Data;

public sealed record LocalDatabaseBootstrapResult(ConnectionProfile Profile, bool Created, string? InitialAdminPassword);

/// <summary>Creates a new isolated local database. Never migrates, replaces or clears an existing DB.</summary>
public sealed class LocalDatabaseBootstrapper
{
    public const int SchemaVersion = 93;
    private const string EngineKey = "local-database.key";
    private const string AdminKey = "initial-admin.key";
    private readonly string _directory;
    private readonly string _runtime;
    private readonly string _databaseFileName;
    public string DatabasePath => Path.Combine(_directory,"data",_databaseFileName);

    public LocalDatabaseBootstrapper(string? directory = null, string? runtime = null, string databaseFileName = "DATA.FDB")
    {
        if(Path.GetFileName(databaseFileName)!=databaseFileName||!databaseFileName.EndsWith(".fdb",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid database filename.",nameof(databaseFileName));
        _databaseFileName=databaseFileName;
        _directory=Path.GetFullPath(directory??ConnectionProfileStore.DirectoryPath);
        _runtime=Path.GetFullPath(runtime??FirebirdRuntime.HomeDirectory);
    }

    public void AcknowledgeAdminCredentials() => File.Delete(Path.Combine(_directory,AdminKey));

    public async Task<LocalDatabaseBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        await using var guard=await LockAsync(cancellationToken);
        var profileFile=Path.Combine(_directory,"connection.json");
        if(File.Exists(profileFile))
        {
            var existing=ConnectionProfileStore.Load(DatabasePath,_directory);
            var initial=existing.UseEmbedded&&Path.GetFullPath(existing.DatabasePath).Equals(DatabasePath,StringComparison.OrdinalIgnoreCase)
                ?LocalDatabaseSecrets.Read(_directory,AdminKey):null;
            if(initial is {Length:>0}&&File.Exists(DatabasePath))
                initial=await UpdateUnusedInitialPasswordAsync(existing,initial,cancellationToken);
            return new(existing,false,initial);
        }
        if(File.Exists(DatabasePath)&&!File.Exists(Path.Combine(_directory,EngineKey)))
            throw new InvalidOperationException("Đã có dữ liệu tại thư mục cài đặt. Ứng dụng không ghi đè dữ liệu này.");

        var password=LocalDatabaseSecrets.Read(_directory,EngineKey);
        if(password is null)
        {
            password=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            LocalDatabaseSecrets.Create(_directory,EngineKey,password);
        }
        var adminPassword=LocalDatabaseSecrets.Read(_directory,AdminKey);
        if(!File.Exists(DatabasePath))
        {
            adminPassword=string.Empty;
            LocalDatabaseSecrets.Create(_directory,AdminKey,adminPassword,overwrite:true);
        }
        var profile=new ConnectionProfile("localhost",3050,DatabasePath,"SYSDBA",password,true);
        var created=false;
        if(!File.Exists(DatabasePath))
        {
            await CreateDatabaseFileAsync(profile, adminPassword!, cancellationToken);
            created=true;
        }
        else
        {
            await ValidateAsync(profile,cancellationToken); // Recovery after DB move, before profile publication.
            if(adminPassword is {Length:>0})
                adminPassword=await UpdateUnusedInitialPasswordAsync(profile,adminPassword,cancellationToken);
        }

        var temporary=profileFile+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new
            {
                profile.Host,profile.Port,profile.DatabasePath,profile.UserName,profile.UseEmbedded,UseLocalCredential=true
            }),cancellationToken);
            File.Move(temporary,profileFile);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
        return new(profile,created,adminPassword);
    }

    public async Task<ConnectionProfile> CreateNewAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Path.GetExtension(fullPath).Equals(".fdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tên cơ sở dữ liệu phải có đuôi .fdb.");
        if (File.Exists(fullPath))
            throw new InvalidOperationException("File đã tồn tại. Hãy chọn tên khác.");

        var profile = new ConnectionProfile("localhost", 3050, fullPath, "SYSDBA",
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), true);
        await CreateDatabaseFileAsync(profile, string.Empty, cancellationToken);
        return profile;
    }

    private async Task CreateDatabaseFileAsync(ConnectionProfile profile, string adminPassword,
        CancellationToken cancellationToken)
    {
        foreach(var required in new[]{"fbclient.dll","plugins/engine13.dll","firebird.msg"})
            if(!File.Exists(Path.Combine(_runtime,required)))
                throw new FileNotFoundException("Thiếu runtime Firebird để tạo dữ liệu mới: "+required);
        Directory.CreateDirectory(Path.GetDirectoryName(profile.DatabasePath)!);
        var stage=Path.Combine(Path.GetDirectoryName(profile.DatabasePath)!,".initializing-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var stagedProfile=profile with { DatabasePath=Path.Combine(stage,Path.GetFileName(profile.DatabasePath)) };
            var builder=Builder(stagedProfile);
            await Task.Run(()=>FbConnection.CreateDatabase(builder.ConnectionString,16384,true,false),cancellationToken);
            foreach(var name in new[]{"schema-v93.sql","system-v93.sql","defaults-v93.sql"})
            {
                var script=Path.Combine(stage,name);
                await using(var resource=typeof(LocalDatabaseBootstrapper).Assembly.GetManifestResourceStream("Kazuko.Bootstrap."+name)
                    ??throw new InvalidOperationException("Thiếu bộ khởi tạo dữ liệu."))
                await using(var target=File.Create(script))
                    await resource.CopyToAsync(target,cancellationToken);
                await ExecuteScriptAsync(stagedProfile,script,cancellationToken);
            }
            await CreateAdministratorAsync(stagedProfile,adminPassword,cancellationToken);
            await ValidateAsync(stagedProfile,cancellationToken);
            File.Move(stagedProfile.DatabasePath,profile.DatabasePath); // Atomic, no overwrite.
        }
        finally
        {
            // Only remove this invocation's private staging directory.
            if(Directory.Exists(stage))Directory.Delete(stage,true);
        }
    }

    private async Task<FileStream> LockAsync(CancellationToken ct)
    {
        var until=DateTime.UtcNow.AddMinutes(2);
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            try {return new FileStream(Path.Combine(_directory,".bootstrap.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException) when(DateTime.UtcNow<until){await Task.Delay(100,ct);}
        }
    }

    private FbConnectionStringBuilder Builder(ConnectionProfile profile)=>new()
    {
        Database=profile.DatabasePath,UserID=profile.UserName,Password=profile.Password,Charset="UTF8",
        Dialect=3,Pooling=false,ServerType=FbServerType.Embedded,ClientLibrary=Path.Combine(_runtime,"fbclient.dll")
    };

    private async Task ExecuteScriptAsync(ConnectionProfile profile,string script,CancellationToken ct)
    {
        // Parse SET TERM/COMMIT inside the supplied schema, without passing Unicode
        // Windows paths through the ANSI command line of the native isql utility.
        var parsed=new FbScript(await File.ReadAllTextAsync(script,ct));
        parsed.Parse();
        await using var connection=new FbConnection(Builder(profile).ConnectionString);
        var batch=new FbBatchExecution(connection);
        batch.AppendSqlStatements(parsed);
        await batch.ExecuteAsync(cancellationToken:ct);
    }

    private async Task CreateAdministratorAsync(ConnectionProfile profile,string password,CancellationToken ct)
    {
        var salt=RandomNumberGenerator.GetBytes(16);
        var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32);
        await using var connection=new FbConnection(Builder(profile).ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO USERS(USERNAME,PASSWORD_HASH,PASSWORD_SALT,PASSWORD_ITERATIONS,DISPLAY_NAME,
                ROLE_CODE,BRANCH_CODE,BRANCH_NAME,COMPANY_ID,ROLE_ID,DEFAULT_BRANCH_ID,GROUP_ID)
            VALUES('admin',@Hash,@Salt,210000,'Quản trị viên','ADMIN','CN_TRUNG_TAM','Chi nhánh chính',1,1,1,1)
            """,new{Hash=Convert.ToHexString(hash),Salt=Convert.ToHexString(salt)},transaction,cancellationToken:ct));
        foreach(var function in SystemFunctionCatalog.All)
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO GROUP_PERMISSIONS(COMPANY_ID,GROUP_ID,FUNCTION_CODE,CAN_VIEW,CAN_ADD,CAN_EDIT,CAN_DELETE)
                VALUES(1,1,@Code,TRUE,TRUE,TRUE,TRUE)
                """,new{function.Code},transaction,cancellationToken:ct));
        await transaction.CommitAsync(ct);
    }

    private async Task<string?> UpdateUnusedInitialPasswordAsync(ConnectionProfile profile,string previousPassword,CancellationToken ct)
    {
        // Only an automatically generated initial password, still unused and unchanged,
        // may be converted. Never reset an account that has already signed in.
        await using var connection=new FbConnection(Builder(profile).ConnectionString);
        await connection.OpenAsync(ct);
        var admin=await connection.QuerySingleOrDefaultAsync<InitialAdministrator>(new CommandDefinition("""
            SELECT PASSWORD_HASH AS Hash,PASSWORD_SALT AS Salt,PASSWORD_ITERATIONS AS Iterations
            FROM USERS WHERE USERNAME='admin' AND ROLE_CODE='ADMIN' AND LAST_LOGIN_AT IS NULL
            """,cancellationToken:ct));
        if(admin is null)return previousPassword;
        var salt=Convert.FromHexString(admin.Salt);
        var stored=Convert.FromHexString(admin.Hash);
        var oldHash=Rfc2898DeriveBytes.Pbkdf2(previousPassword,salt,admin.Iterations,HashAlgorithmName.SHA256,32);
        var emptyHash=Rfc2898DeriveBytes.Pbkdf2("",salt,admin.Iterations,HashAlgorithmName.SHA256,32);
        if(!CryptographicOperations.FixedTimeEquals(stored,oldHash)&&!CryptographicOperations.FixedTimeEquals(stored,emptyHash))
        {
            AcknowledgeAdminCredentials();return null;
        }
        await using var transaction=connection.BeginTransaction();
        var changed=await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE USERS SET PASSWORD_HASH=@NewHash,UPDATED_AT=CURRENT_TIMESTAMP
            WHERE USERNAME='admin' AND LAST_LOGIN_AT IS NULL AND PASSWORD_HASH=@Hash AND PASSWORD_SALT=@Salt
            """,new{NewHash=Convert.ToHexString(emptyHash),admin.Hash,admin.Salt},transaction,cancellationToken:ct));
        if(changed!=1)return previousPassword;
        await AuditLogWriter.WriteAsync(connection,transaction,"INITIAL_ADMIN_PASSWORD_UPDATED",
            userName:"system",entityType:"USERS",entityId:"admin",eventData:"Unused installation account default updated.",cancellationToken:ct);
        await transaction.CommitAsync(ct);
        LocalDatabaseSecrets.Create(_directory,AdminKey,"",overwrite:true);
        return "";
    }

    private sealed class InitialAdministrator
    {
        public string Hash {get;set;}="";
        public string Salt {get;set;}="";
        public int Iterations {get;set;}
    }

    private async Task ValidateAsync(ConnectionProfile profile,CancellationToken ct)
    {
        await using var connection=new FbConnection(Builder(profile).ConnectionString);
        await connection.OpenAsync(ct);
        var version=await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS",cancellationToken:ct));
        var admins=await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM USERS WHERE USERNAME='admin' AND IS_ACTIVE=TRUE",cancellationToken:ct));
        if(version!=SchemaVersion||admins!=1)throw new InvalidOperationException("Dữ liệu khởi tạo chưa đầy đủ. Không thay thế dữ liệu hiện có.");
    }
}
