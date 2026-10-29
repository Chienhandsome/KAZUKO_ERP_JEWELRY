# Read-only money-flow reconciliation

Reads the reviewed workspace `data/DATA.FDB` using one Firebird `Read | Concurrency | Wait` transaction. Verifies `MON$READ_ONLY=1` for that transaction before reading any business table. Uses SELECT statements directly; does not call application services, save profiles, seed, migrate, or create fault triggers.

## Invocation

```powershell
dotnet restore tools/Kazuko.MoneyFlow.ReadOnlyAudit/Kazuko.MoneyFlow.ReadOnlyAudit.csproj
dotnet build tools/Kazuko.MoneyFlow.ReadOnlyAudit/Kazuko.MoneyFlow.ReadOnlyAudit.csproj --no-restore -p:BuildInParallel=false
dotnet tools/Kazuko.MoneyFlow.ReadOnlyAudit/bin/Debug/net8.0/Kazuko.MoneyFlow.ReadOnlyAudit.dll D:\Working\KAZUKO_ERP_JEWELRY\data\DATA.FDB D:\Working\artifacts\money-flow-gd4\new-run
```

Output must be a **new** subdirectory of `D:\Working\artifacts\money-flow-gd4`; refuses overwrite. DB path without `--test` must match this workspace DATA.FDB and the configured profile must point to that local database. Credentials come from the existing environment/profile loader and are never exported. The process forces the explicit database path and localhost connection, without persisting that profile.

`--test` permits an explicitly restored `test91_moneyflow_*.fdb` directly inside `D:\Working\artifacts\money-flow-gd3`. `--probe` exports read-only metadata/table counts/constraints and exits before reconciliation. Incomplete inputs fail the run; do not treat a missing results.json or nonzero exit code as a completed audit.

## Interpretation

- `results.json` contains metadata, row counts and16 reconciliation sections. SELECT columns omit names, phone numbers, bank numbers, passwords and free-text notes. Evidence refers to IDs/codes.
- `selects.sql` records business data SELECTs and supplemental reconciliation queries; monitoring, schema and constraint queries are in Program.cs. All are executed within the same transaction in the actual program. Do not run individual SQL statements under implicit read/write transactions and assume equivalent snapshot semantics.
- Funds use allocations for linked vouchers and header for direct vouchers. Settlement totals count distinct voucher IDs. Orders use payments and effective checkout offsets separately. Receipt equations include noncash offsets and original source debt.
- Exception counts are not monetary loss totals. Retry candidates are heuristic, not proof of duplicate intent. There is no persisted manual bank/fund selection to reconstruct intent. A failed posting that rolled back cannot be counted from committed payment tables.
- Opening a Firebird DB in a read-only **transaction** can still affect engine file/header bookkeeping. This tool guarantees no business DML/DDL, not byte-for-byte immutability of the physical file. Never restore a tracked database file from Git merely to remove a binary change.
- Last verified runtime results and limitations are in [GĐ4 report](../../docs/audits/2026-10-28-money-flow-gd4-data-reconciliation.md).
