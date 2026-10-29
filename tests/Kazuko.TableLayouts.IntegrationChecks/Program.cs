using System.Security.Cryptography;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Settings;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Settings;

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "verify-restart") || !Path.GetFileName(args[0]).StartsWith("test89_", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Use only a restored test89_*.fdb; never DATA.FDB.");
var path = Path.GetFullPath(args[0]);
FirebirdDatabase.ApplyProfileForCurrentProcess(ConnectionProfileStore.Load(path) with { DatabasePath = path, UseEmbedded = false });
await using var connection = FirebirdDatabase.CreateConnection();
await connection.OpenAsync();
var ids = (await connection.QueryAsync<long>("SELECT FIRST 2 ID FROM USERS WHERE IS_ACTIVE=TRUE ORDER BY ID")).ToArray();
Check(ids.Length == 2, "two users available in restored fixture");
var branches = (await connection.QueryAsync<Branch>("SELECT ID, COMPANY_ID AS CompanyId FROM BRANCHES ORDER BY ID")).ToArray();
var branch = branches[0];
var session = new AuthenticatedSession(ids[0], "test", "Test", "ADMIN", branch.Id, "TEST", "Test");
var repository = new FirebirdTableColumnLayoutRepository(session);
if (args.Length == 2)
{
    var restored = (await repository.LoadAsync())["integration.restart"];
    Check(restored.Columns[0].Width == 237 && restored.Columns[1].IsVisible == false && restored.Columns[0].DisplayIndex == 1,
        "a new process restores the layout saved by the prior process");
    return;
}
var suffix = Guid.NewGuid().ToString("N")[..10];
var key = "integration.table-layout." + suffix;
var first = new DataGridColumnLayoutSettings([new("code", true, 1, 237), new("name", false, 0, 180)]);
await repository.SaveAsync(key, first);
var reopened = new FirebirdTableColumnLayoutRepository(session);
var loaded = (await reopened.LoadAsync())[key];
Check(loaded.Columns.SequenceEqual(first.Columns), "new repository restores visibility, order and width");
Check(!(await new FirebirdTableColumnLayoutRepository(session with { UserId = ids[1] }).LoadAsync()).ContainsKey(key), "another user cannot see the layout");
var company2 = await connection.QuerySingleAsync<long>("INSERT INTO COMPANIES(CODE,NAME) VALUES(@code,'Table layout test company') RETURNING ID", new { code = "TL_" + suffix });
var branch2 = await connection.QuerySingleAsync<long>("INSERT INTO BRANCHES(COMPANY_ID,CODE,NAME) VALUES(@company2,@code,'Table layout test branch') RETURNING ID", new { company2, code = "TL_" + suffix });
var otherCompany = new Branch(branch2, company2);
Check(!(await new FirebirdTableColumnLayoutRepository(session with { BranchId = otherCompany.Id }).LoadAsync()).ContainsKey(key), "another company cannot see the layout");
var sameBranch = await connection.QuerySingleAsync<long>("INSERT INTO BRANCHES(COMPANY_ID,CODE,NAME) VALUES(@companyId,@code,'Table layout same-company branch') RETURNING ID", new { companyId = branch.CompanyId, code = "TLS_" + suffix });
Check((await new FirebirdTableColumnLayoutRepository(session with { BranchId = sameBranch }).LoadAsync()).ContainsKey(key), "branches in the same company share user settings");
await reopened.SaveAsync(key.ToUpperInvariant(), first with { Columns = [new("code", true, 0, 310)] });
var revision = await connection.ExecuteScalarAsync<long>("SELECT REVISION FROM USER_TABLE_LAYOUTS WHERE USER_ID=@userId AND COMPANY_ID=@companyId AND SETTINGS_KEY=@key", new { userId = session.UserId, companyId = branch.CompanyId, key });
Check(revision == 2, "case-normalized upsert updates one row and increments revision");
var wide = new DataGridColumnLayoutSettings(Enumerable.Range(0, 400)
    .Select(i => new DataGridColumnSetting("column_" + i + new string('x', 90), true, i, 120)).ToArray());
await repository.SaveAsync(key + ".large", wide);
Check((await repository.LoadAsync())[key + ".large"].Columns.Count == 400, "UTF8 text BLOB supports layouts larger than VARCHAR limits");
var profile = FirebirdDatabase.Profile;
try
{
    FirebirdDatabase.ApplyProfileForCurrentProcess(profile with { DatabasePath = path + ".different-db" });
    await repository.SaveAsync(key, first);
    Check((await repository.LoadAsync()).ContainsKey(key), "queued repository remains pinned to the original database");
}
finally { FirebirdDatabase.ApplyProfileForCurrentProcess(profile); }
await connection.ExecuteAsync("CREATE EXCEPTION EX_TEST_LAYOUT 'Forced layout failure'");
await connection.ExecuteAsync("CREATE TRIGGER TR_TEST_LAYOUT FOR USER_TABLE_LAYOUTS ACTIVE BEFORE UPDATE POSITION 0 AS BEGIN EXCEPTION EX_TEST_LAYOUT; END");
try
{
    try { await repository.SaveAsync(key, first with { Columns = [new("code", true, 0, 999)] }); throw new Exception("Expected rejection."); }
    catch (FirebirdSql.Data.FirebirdClient.FbException) { }
    Check((await repository.LoadAsync())[key].Columns.SequenceEqual(first.Columns), "failed DB update rolls back and preserves the prior snapshot");
}
finally
{
    await connection.ExecuteAsync("DROP TRIGGER TR_TEST_LAYOUT");
    await connection.ExecuteAsync("DROP EXCEPTION EX_TEST_LAYOUT");
}
var checksum = await connection.ExecuteScalarAsync<string>("SELECT CHECKSUM_VALUE FROM SCHEMA_MIGRATIONS WHERE VERSION_NO=93");
var artifact = Path.Combine(Directory.GetCurrentDirectory(), "database", "migrations", "0093__user_table_layouts.sql");
Check(checksum == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifact))), "migration ledger matches the full artifact checksum");
await repository.SaveAsync("integration.restart", first);
Console.WriteLine("ALL TABLE LAYOUT DATABASE CHECKS PASSED; only restored test DB written.");
static void Check(bool value, string label) { if (!value) throw new Exception("FAILED: " + label); Console.WriteLine("PASS: " + label); }
sealed record Branch(long Id, long CompanyId);
