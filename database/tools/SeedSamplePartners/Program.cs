using Dapper;
using System.Data;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Suppliers;
using Kazuko.Infrastructure.Customers;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Suppliers;

// Read-only by default. Explicit --apply inserts missing sample partners through
// the normal repositories (validation, generated codes and audit included).
// Re-running does not duplicate or overwrite partners with the same seed marker.
if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--apply"))
    throw new ArgumentException("Usage: SeedSamplePartners <database-path> [--apply]");
var databasePath = Path.GetFullPath(args[0]);
if (!File.Exists(databasePath)) throw new FileNotFoundException(databasePath);
var root = new DirectoryInfo(databasePath).Parent!.Parent!.FullName;
Environment.SetEnvironmentVariable("KAZUKO_FIREBIRD_HOME",
    Path.Combine(root, "firebird", "Firebird-5.0.4.1812-0-windows-x64"));
var profile = ConnectionProfileStore.Load(databasePath);
FirebirdDatabase.ApplyProfileForCurrentProcess(profile with { DatabasePath = databasePath, UseEmbedded = false });
Console.WriteLine($"Database: {FirebirdDatabase.DatabasePath}");
await using var db = FirebirdDatabase.CreateConnection();
await db.OpenAsync();
var session = await db.QuerySingleAsync<AuthenticatedSession>("""
    SELECT FIRST 1 U.ID AS UserId, U.USERNAME AS Username,
           COALESCE(U.DISPLAY_NAME, U.USERNAME) AS DisplayName, R.CODE AS RoleCode,
           B.ID AS BranchId, B.CODE AS BranchCode, B.NAME AS BranchName
    FROM USERS U JOIN ROLES R ON R.ID=U.ROLE_ID
    JOIN BRANCHES B ON B.ID=U.DEFAULT_BRANCH_ID
    WHERE U.IS_ACTIVE=TRUE AND R.CODE='ADMIN' AND B.IS_ACTIVE=TRUE
    ORDER BY U.ID
    """);
var customers = new FirebirdCustomerRepository();
var suppliers = new FirebirdSupplierRepository();
var customerGroup = (await customers.GetGroupsAsync()).OrderByDescending(x => x.IsDefault).First();
var supplierGroup = (await suppliers.GetGroupsAsync()).OrderByDescending(x => x.IsDefault).First();
const string marker = "SAMPLE_PARTNERS_20261019";
const string note = marker + " - Dữ liệu mẫu theo yêu cầu; thông tin liên hệ giả lập, không dùng liên lạc thực tế.";
var supplierNames = new[] { "Vàng bạc Minh Phát (Mẫu)", "Kim hoàn An Khang (Mẫu)", "Nguyên liệu Hoàng Gia (Mẫu)", "Đá quý Bảo Ngọc (Mẫu)", "Trang sức Phúc Lộc (Mẫu)" };
var customerNames = new[] { "Nguyễn Minh Anh (Mẫu)", "Trần Hoàng Nam (Mẫu)", "Lê Thu Hà (Mẫu)", "Phạm Ngọc Linh (Mẫu)", "Võ Quốc Bảo (Mẫu)" };
var apply = args.Length == 2;
// Existing customer persistence passes untyped DBNull into DynamicParameters.
// Handle it locally in this one-shot tool, without changing application behavior.
SqlMapper.AddTypeHandler(new NullParameterHandler());
Console.WriteLine($"Mode: {(apply ? "APPLY" : "READ ONLY")}; groups: {supplierGroup.Code}, {customerGroup.Code}");
for (var i = 0; i < 5; i++)
{
    await SeedSupplier(i);
    await SeedCustomer(i);
}
if (apply)
{
    var supplierCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SUPPLIERS WHERE NOTES STARTING WITH @marker", new { marker });
    var customerCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CUSTOMERS WHERE NOTES STARTING WITH @marker", new { marker });
    if (supplierCount != 5 || customerCount != 5) throw new Exception("Seed verification failed.");
    Console.WriteLine("VERIFIED: 5 suppliers, 5 customers.");
}

async Task SeedSupplier(int i)
{
    var name = supplierNames[i];
    var existing = await db.QuerySingleOrDefaultAsync<string>(
        "SELECT CODE FROM SUPPLIERS WHERE NAME=@name AND NOTES STARTING WITH @marker", new { name, marker });
    if (existing != null) { Console.WriteLine($"EXISTS {existing}: {name}"); return; }
    var request = new SupplierSaveRequest(null, name, supplierGroup.Id, "", "", $"00000001{i + 1:00}",
        $"supplier{i + 1}@example.invalid", "", "", "", note, true, "", "");
    var warning = await suppliers.FindDuplicateWarningAsync(request);
    if (warning.Length > 0) throw new InvalidOperationException(warning);
    if (!apply) { Console.WriteLine($"PLAN supplier: {name}"); return; }
    var saved = await suppliers.SaveAsync(request, session);
    if ((await suppliers.GetByIdAsync(saved.Id))?.Name != name) throw new Exception("Supplier readback failed.");
    Console.WriteLine($"CREATED {saved.Code}: {saved.Name}");
}

async Task SeedCustomer(int i)
{
    var name = customerNames[i];
    var existing = await db.QuerySingleOrDefaultAsync<string>(
        "SELECT CODE FROM CUSTOMERS WHERE NAME=@name AND NOTES STARTING WITH @marker", new { name, marker });
    if (existing != null) { Console.WriteLine($"EXISTS {existing}: {name}"); return; }
    var request = new CustomerSaveRequest(null, name, customerGroup.Id, "", $"00000002{i + 1:00}",
        $"customer{i + 1}@example.invalid", null, 0, "", note, "", "", "", "",
        new DateTime(2026, 10, 19), 0, true, null, null, "", "", "", "");
    var warning = await customers.FindDuplicateWarningAsync(request);
    if (warning.Length > 0) throw new InvalidOperationException(warning);
    if (!apply) { Console.WriteLine($"PLAN customer: {name}"); return; }
    var saved = await customers.SaveAsync(request, session);
    if ((await customers.GetByIdAsync(saved.Id))?.Name != name) throw new Exception("Customer readback failed.");
    Console.WriteLine($"CREATED {saved.Code}: {saved.Name}");
}

sealed class NullParameterHandler : SqlMapper.TypeHandler<DBNull>
{
    public override void SetValue(IDbDataParameter parameter, DBNull? value) => parameter.Value = DBNull.Value;
    public override DBNull Parse(object value) => DBNull.Value;
}
