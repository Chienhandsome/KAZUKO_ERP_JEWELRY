using Dapper;
using System.IO;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.Authentication;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.UserManagement;

if (args is ["--unit"]) { await AuthorizationChecks.RunAsync(); return; }
if (args is ["--ui"]) { UiAuthorizationChecks.Run(); return; }
if (args.Length != 1 || !Path.GetFileName(args[0]).StartsWith("kazuko_user_verify_"))
    throw new InvalidOperationException("Run only against the restored verification database.");
FirebirdDatabase.ApplyProfileForCurrentProcess(new ConnectionProfile("localhost", 3050,
    args[0], "SYSDBA", Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_PASSWORD")!));
await using var db = FirebirdDatabase.CreateConnection();
await db.OpenAsync();
var employeeId = await db.ExecuteScalarAsync<long>("""
    INSERT INTO HR_EMPLOYEES (COMPANY_ID, CODE, FULL_NAME)
    VALUES ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), 'VERIFY_USER_ACCOUNT', 'Verification account') RETURNING ID
    """);
var creatorId = await db.QuerySingleAsync<long>("SELECT FIRST 1 ID FROM USERS ORDER BY ID");
var branchId = await db.QuerySingleAsync<long>("SELECT FIRST 1 ID FROM BRANCHES WHERE IS_ACTIVE=TRUE ORDER BY ID DESC");
var groupId = await db.QuerySingleAsync<long>("SELECT ID FROM USER_GROUPS WHERE CODE='CASHIER'");
var repo = new FirebirdUserManagementRepository();
var request = new UserSaveRequest(null, employeeId, null, "verify@example.invalid", groupId,
    "VERIFY.CASHIER", "Verify-Password-123", "test create", true, creatorId, branchId);
var saved = await repo.SaveUserAsync(request);
var row = await db.QuerySingleAsync("""
    SELECT U.DEFAULT_BRANCH_ID, U.BRANCH_CODE, U.BRANCH_NAME, U.ROLE_CODE, R.CODE AS ACTUAL_ROLE, B.CODE AS EXPECTED_CODE, B.NAME AS EXPECTED_NAME
    FROM USERS U JOIN ROLES R ON R.ID=U.ROLE_ID JOIN BRANCHES B ON B.ID=U.DEFAULT_BRANCH_ID WHERE U.ID=@Id
    """, new { saved.Id });
Check((long)row.DEFAULT_BRANCH_ID == branchId, "session branch");
Check((string)row.BRANCH_CODE == (string)row.EXPECTED_CODE && (string)row.BRANCH_NAME == (string)row.EXPECTED_NAME, "legacy branch fields");
Check((string)row.ROLE_CODE == "SALE" && (string)row.ACTUAL_ROLE == "SALE", "cashier role is not creator admin");
Check(saved.WarehouseId is null && saved.Username == "verify.cashier", "no warehouse and normalized username");
Check((await repo.GetUsersAsync(groupId)).Any(x => x.Id == saved.Id), "created user visible in group");
var auth = new FirebirdAuthenticationService();
Check((await auth.SignInAsync(saved.Username, request.Password!, (string)row.BRANCH_CODE)).IsSuccess, "new account login");
await repo.SaveUserAsync(request with { Id = saved.Id, Password = null, Notes = "test edit" });
Check((await auth.SignInAsync(saved.Username, request.Password!, (string)row.BRANCH_CODE)).IsSuccess, "edit preserves password");
await repo.SaveUserAsync(request with { Id = saved.Id, Password = "Changed-Password-123" });
Check((await auth.SignInAsync(saved.Username, "Changed-Password-123", (string)row.BRANCH_CODE)).IsSuccess, "edit password login");
try { await repo.SaveUserAsync(request); throw new Exception("duplicate account accepted"); }
catch (InvalidOperationException ex) when (ex.Message.Contains("đã tồn tại")) { Console.WriteLine("PASS duplicate account rejected"); }
Console.WriteLine("USER_ACCOUNT_VERIFICATION_PASSED");
await AuthorizationChecks.RunDatabaseAsync(db, saved.Id, groupId, branchId, (string)row.BRANCH_CODE, creatorId);
static void Check(bool passed, string name) { if (!passed) throw new Exception(name); Console.WriteLine($"PASS {name}"); }
