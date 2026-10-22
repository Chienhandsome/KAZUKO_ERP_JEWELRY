# Payroll integration checks

Run from the repository root with the configured Firebird profile and KAZUKO closed:

```powershell
dotnet run --project tests/Kazuko.Payroll.IntegrationChecks
```

The tool backs up the configured database, restores a separate copy under
`artifacts/payroll-check/`, applies migration 82 to the copy, resets the fixture
month in that copy, and checks calculation, adjustment preservation, stale
revision/source rejection, closing, frozen snapshots, reopening and audit.
It never calculates or writes payroll data in the source database.
Requires a source database with migration 81 and at least one employee/two shifts.
