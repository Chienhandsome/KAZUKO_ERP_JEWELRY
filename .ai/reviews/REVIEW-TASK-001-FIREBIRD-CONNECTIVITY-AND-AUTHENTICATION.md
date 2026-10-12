# Review - TASK-001

## Summary

The implementation correctly adds the approved `FirebirdSql.Data.FirebirdClient` 10.3.4 package only to Infrastructure, bundles the supplied Embedded runtime into App output, adds branch selection, uses parameterized Firebird SQL, verifies PBKDF2-SHA256 hashes with fixed-time comparison, removes the development authentication composition fallback, and keeps database schema unchanged. An independent solution build succeeded with zero warnings/errors.

## Critical Issues

None.

## Functional Issues

None identified from the implementation and reported verification scenarios.

## Database / Firebird Issues

1. `ConnectionProfileStore.FromEnvironment` has the literal fallback `"masterkey"` in source. This violates the task acceptance criterion that no plaintext password/connection secret is source-controlled. Remove the fallback and require `KAZUKO_FIREBIRD_PASSWORD` (or an existing secure, local provisioning mechanism) before a connection can be attempted. A missing password must return a safe Vietnamese configuration/login-service error and must not display the secret/path/SQL.
2. `AuditLogWriter.Write` uses synchronous `connection.Open()` and `command.ExecuteNonQuery()` from `FirebirdAuthenticationService.SignInAsync`. Because `SignInAsync` resumes on the WPF synchronization context, these calls can freeze the login UI during a slow database operation. Convert it to `WriteAsync`, use provider async APIs, await it from the authentication service, and retain the existing behavior that audit failure does not conceal the original authentication result.

## WPF / UI Issues

None identified in the changed Login View/ViewModel scope.

## Architecture Issues

None identified. The Firebird implementation remains in Infrastructure and WPF contains no SQL.

## Code Quality Issues

The authentication checks are reported in Task Notes but there is no committed/reproducible test harness. This is acceptable for the current task scope, but the next data-access task should add an approved automated test/project strategy.

## Unrelated Changes Detected

No unrelated implementation files were identified within TASK-001's allowed scope. Firebird runtime/data binaries remain ignored by Git.

## Required Fixes

1. Remove the source-controlled `masterkey` fallback and handle missing engine credentials safely.
2. Make audit writes asynchronous end-to-end from the login path.
3. Re-run the four documented authentication checks and `dotnet build KAZUKO_ERP_JEWELRY.sln`.
4. Update TASK-001 Notes with the result, then move the task to `done` only after these fixes pass.

## Optional Improvements

Document a future installer-time DPAPI-backed provisioning flow for the engine credential; do not add it as an unrelated implementation change in this task.

## Follow-up Review

Both required code fixes were verified: `masterkey` no longer appears in application source and Firebird audit writes now use `WriteAsync` with async open/execute calls awaited from authentication. After closing the process that had locked Debug DLLs, an independent `dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Debug` succeeded with 0 warnings and 0 errors.

## Review Result

Ready to merge
