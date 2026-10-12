# TASK-001 - Firebird Connectivity and Authentication

## Assigned Agent

Cursor

## Status

DONE

## Goal

Replace the current development-only login implementation with an authenticated Firebird-backed login, while preserving the existing WPF login flow, UI, database schema, and Core contracts unless a documented incompatibility requires a narrowly scoped change.

## Context

The current WPF application uses `DevelopmentAuthenticationService`, accepts the development account `admin` with an empty password, and has no Firebird provider, connection factory, repository, migration runner, or database configuration in this repository.

The approved WinForms reference project is located at:

```text
D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0
```

It is read-only reference material for this task. Inspect its authentication flow, Firebird provider/version, connection configuration, and `USERS` query behavior before designing the WPF implementation. Do not change that project.

No active task owns any of the Allowed Files at task creation. Re-check `.ai/tasks/doing/` before moving this task to DOING.

## Requirements

1. Read `.ai/PROJECT.md`, `.ai/RULES.md`, and all project docs required by `AGENTS.md` before coding.
2. First document the legacy findings in this task's Notes: Firebird provider/package and version, database location/configuration approach, relevant `USERS` fields, password-verification behavior, and error-handling behavior.
3. Reuse the current `IAuthenticationService` and `AuthenticationResult` contract if it is sufficient. Change it only if a concrete legacy requirement cannot fit, and document why.
4. Add a Firebird authentication implementation in Infrastructure; do not put database logic in a View, ViewModel, or code-behind.
5. Add a narrowly scoped Firebird connection/configuration component in Infrastructure. It must not expose a plaintext secret in source control.
6. Switch `App.xaml.cs` composition from `DevelopmentAuthenticationService` to the Firebird implementation only after the connection/configuration path is ready.
7. Use async APIs where the selected provider supports them; never block the WPF UI thread for database I/O.
8. Show users a safe, Vietnamese failure message. Log or retain technical diagnostics only through an approved diagnostics mechanism; do not expose a connection string, database path, SQL, or exception details in the UI.
9. Remove the development-only account hint from the login UI/README only if the production authentication path is functional and a replacement operator instruction is supplied.
10. Do not add a fallback that silently authenticates `admin` when Firebird is unavailable.

## UI Requirements

- Preserve `LoginView.xaml`, its resource usage, visual structure, and theme/font-scale behavior.
- Reuse the existing `ErrorMessage` binding for authentication/connection failures unless a small ViewModel change is required.
- Do not edit shared theme dictionaries, `App.xaml`, `MainWindow.xaml`, `ShellViewModel.cs`, or dashboard files.
- Verify the login form in Light and Dark themes at 100% and 125% font scale after a successful connection is available.

## Database Requirements

- Do not change Firebird schema, tables, triggers, procedures, seed data, or migrations.
- Do not copy, overwrite, repair, or run tools against `data.fdb` or any production database.
- Use parameterized Firebird SQL only; never concatenate username/password input into SQL.
- Open/dispose connection, command, reader, and transaction objects correctly.
- A login read normally does not need a transaction. If the legacy flow writes login audit data, match its transaction behavior only after confirming the exact legacy implementation.
- Do not assume SQL Server syntax or generated-ID behavior.
- Before connecting to any database other than the approved read-only reference copy, obtain explicit operator direction for its path/host and credentials.

## Allowed Files

- `.ai/tasks/todo/TASK-001-FIREBIRD-CONNECTIVITY-AND-AUTHENTICATION.md` (move/update task state and notes only)
- `.ai/PROJECT.md` (only to document newly established Firebird conventions after they are implemented)
- `.gitignore` (only to exclude the copied development database from Git)
- `README.md` (only to replace obsolete development-login instructions after successful implementation)
- `src/Kazuko.Infrastructure/Kazuko.Infrastructure.csproj` (only if explicit package-install approval is recorded)
- `src/Kazuko.Infrastructure/Authentication/**`
- `src/Kazuko.Infrastructure/Data/**` (new files only)
- `src/Kazuko.Core/Authentication/AuthenticationContracts.cs` (only if a documented incompatibility requires it)
- `src/Kazuko.App/App.xaml.cs`
- `src/Kazuko.App/Kazuko.App.csproj` (only to copy the user-provisioned Firebird Embedded runtime to build output)
- `src/Modules/Kazuko.Modules.Authentication/LoginViewModel.cs` (only for loading/error state necessary for actual authentication)
- `src/Modules/Kazuko.Modules.Authentication/LoginView.xaml` (add branch selection and replace development-account help text)
- `data/data.fdb` (one-time development verification copy only; never add this binary file to Git)

## Do Not Modify

- `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0/**`
- Any Firebird database file, schema, migration, SQL script, or seed data
- `src/Kazuko.App/App.xaml`
- `src/Kazuko.App/MainWindow.xaml`
- `src/Kazuko.App/ShellViewModel.cs`
- `src/Kazuko.Shared.UI/**`
- `src/Kazuko.Core/Modules/**`
- `src/Kazuko.Core/Appearance/**`
- `src/Kazuko.Infrastructure/Updates/**`
- `src/Modules/Kazuko.Modules.Dashboard/**`
- `KAZUKO_ERP_JEWELRY.sln`, `Directory.Build.props`, `global.json`
- Any file owned by another active task

## Acceptance Criteria

- Legacy authentication and Firebird connection findings are recorded in task Notes before implementation choices are finalized.
- A Firebird-backed `IAuthenticationService` validates an existing user against the legacy schema using parameterized SQL.
- Invalid credentials and unavailable/misconfigured database result in safe Vietnamese messages; no silent development fallback exists.
- No source-controlled plaintext password, connection secret, or machine-specific production path is introduced.
- No database schema is modified. Login-side data changes are permitted only in the approved WPF development copy at `data/data.fdb`.
- The existing Login → Shell transition succeeds only for a successful Firebird authentication result.
- Existing visual layout, theme resources, and font scaling remain intact.
- The full solution builds with zero warnings/errors.
- Task Notes record the database used for verification and confirm it was not modified.

## Build / Verification

```powershell
dotnet build KAZUKO_ERP_JEWELRY.sln
dotnet run --project src/Kazuko.App/Kazuko.App.csproj
```

Verify, against an explicitly approved non-production/reference Firebird database:

1. Valid existing user reaches the shell.
2. Invalid password remains on Login and shows a safe Vietnamese error.
3. Unknown user remains on Login and shows a safe Vietnamese error.
4. Unavailable/misconfigured database remains on Login and shows a safe Vietnamese error.
5. No database schema changes occur. Expected `LAST_LOGIN_AT`/audit writes are allowed only in the WPF development copy.

## Notes

- Package install authorization: **Approved 2026-09-25.** Add `FirebirdSql.Data.FirebirdClient` version **10.3.4** only to `src/Kazuko.Infrastructure/Kazuko.Infrastructure.csproj`.
- Database deployment/configuration decision: **Approved 2026-09-25.** Create a one-time development verification copy from `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0\data\DATA.FDB` to `D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb`. Do not modify the source file and do not add the copied binary to Git.
- Branch and login-write decision: **Approved 2026-09-25.** Add branch selection to the WPF Login View and match the legacy success behavior, including `LAST_LOGIN_AT` and audit writes, only against the approved WPF development copy.
- Git/worktree prerequisite: the repository currently has no observed Git metadata. Do not initialize Git or create a worktree unless explicitly authorized.
- No overlap is currently approved. This task is complete and moved to `done` after review fixes.

### Review fix checkpoint (2026-09-25) — Cursor

Addressed `.ai/reviews/REVIEW-TASK-001-FIREBIRD-CONNECTIVITY-AND-AUTHENTICATION.md`:

1. Removed source-controlled `"masterkey"` fallback from `ConnectionProfileStore`. Engine password is required via `KAZUKO_FIREBIRD_PASSWORD` only. Missing password → safe Vietnamese message: `Cấu hình kết nối cơ sở dữ liệu chưa đầy đủ. Vui lòng liên hệ quản trị viên.` (no password/path/SQL/stack leaked). LoginViewModel surfaces the same configuration errors when loading branches.
2. `AuditLogWriter.WriteAsync` now uses `OpenAsync` / `ExecuteNonQueryAsync`; `FirebirdAuthenticationService` awaits audit writes. Audit failures still do not conceal the login result.
3. Re-verified against `data\data.fdb` with password supplied only via process environment (not source):
   - VALID admin login → success / `Administrator`
   - Wrong password → Vietnamese credentials error
   - Unknown user → Vietnamese credentials/branch error
   - DB unavailable → safe connection error
   - Missing engine password → safe configuration error
   - Result: **ALL_CHECKS_PASSED**
4. `dotnet build KAZUKO_ERP_JEWELRY.sln` → **succeeded**, 0 warning(s), 0 error(s)

### Implementation checkpoint (2026-09-25) — Cursor

**Decisions applied**

- NuGet: `FirebirdSql.Data.FirebirdClient` 10.3.4 in Infrastructure only.
- Runtime: Embedded from `firebird\Firebird-5.0.4.1812-0-windows-x64\`; `Kazuko.App.csproj` copies runtime into output `firebird\` (root `/firebird` remains gitignored).
- Dev DB: copied once to `data\data.fdb`; `.gitignore` excludes `/data/*.fdb` and `/data/*.FDB`.
- Core contract extended with `BranchOption` + `GetBranchesAsync` + `SignInAsync(..., branchCode)` because legacy branch selection is required and now Allowed on Login UI.
- Composition root uses `FirebirdAuthenticationService` only (no admin fallback on Firebird failure).
- Connection profile: env/`connection.json` non-secret fields; engine password **required** from `KAZUKO_FIREBIRD_PASSWORD` (no hardcoded fallback).
- Login success writes `LAST_LOGIN_AT` + async `AUDIT_LOG` only against the WPF `data\data.fdb` copy.

**Verification database**

- Used: `D:\Working\KAZUKO_ERP_JEWELRY\data\data.fdb`
- Source reference (untouched): `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0\data\DATA.FDB` (11,042,816 bytes / LastWriteTime 2026-09-22 12:03)
- Schema: not modified. Expected login-side row writes only in the WPF copy.

**Automated auth checks (Infrastructure service harness)**

1. Valid `admin` + empty password + branch → success, display `Administrator`
2. Wrong password → failure, Vietnamese credentials message
3. Unknown user → failure, Vietnamese credentials/branch message
4. Missing DB path → failure, safe Vietnamese connection message (no path/SQL/stack in UI message)
5. Missing `KAZUKO_FIREBIRD_PASSWORD` → failure, safe Vietnamese configuration message
- Result: **ALL_CHECKS_PASSED** (after review fixes)

**Build**

- `dotnet build KAZUKO_ERP_JEWELRY.sln` → **succeeded**, 0 warning(s), 0 error(s)

**Files changed / added**

- `.gitignore`, `.ai/PROJECT.md`, `README.md`
- `.ai/tasks/done/TASK-001-FIREBIRD-CONNECTIVITY-AND-AUTHENTICATION.md` (notes + DONE)
- `src/Kazuko.Infrastructure/Kazuko.Infrastructure.csproj` (+ package)
- `src/Kazuko.Infrastructure/Data/*`: `FirebirdRuntime`, `FirebirdDatabase`, `ConnectionProfileStore`, `AuditLogWriter`
- `src/Kazuko.Infrastructure/Authentication/FirebirdAuthenticationService.cs`
- `src/Kazuko.Infrastructure/Authentication/DevelopmentAuthenticationService.cs` (signature update; unused by App)
- `src/Kazuko.Core/Authentication/AuthenticationContracts.cs`
- `src/Kazuko.App/App.xaml.cs`, `src/Kazuko.App/Kazuko.App.csproj`
- `src/Modules/Kazuko.Modules.Authentication/LoginView.xaml`, `LoginViewModel.cs`
- `data/data.fdb` (local gitignored copy only)

### Survey checkpoint (2026-09-25) — Cursor implementation agent

See earlier survey notes retained for legacy findings context.

**Firebird Embedded runtime (WPF repo)**

- `fbclient.dll` exists under `firebird\Firebird-5.0.4.1812-0-windows-x64\`.
- Dev DB present at `data\data.fdb` (gitignored).

**Legacy summary**

- Package `FirebirdSql.Data.FirebirdClient` 10.3.4; Embedded 5.0.4; PBKDF2-SHA256 on `USERS`; branch required; `LAST_LOGIN_AT` + audit on success.
