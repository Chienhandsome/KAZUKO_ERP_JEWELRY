# Money-flow GĐ3 reproduction harness

Runs real Finance/Sales/Warehouse/Production services against an explicitly restored test database. Several assertions intentionally reproduce known defects: success means the observed evidence matches the reproduction, not that financial behavior is correct. See [runtime report](../../docs/audits/2026-10-28-money-flow-gd3-runtime-checks.md).

## Database isolation

- Accepts only existing `test91_moneyflow_*.fdb` files directly inside `D:\Working\artifacts\money-flow-gd3` and schema91.
- Rejects reparse points on that file/directory; forces this process to localhost:3050 and the explicit database path. Never saves an app connection profile.
- Requires normal configured Firebird credentials through the environment. Do not add secrets to arguments/source/logs.
- Restoring a backup and applying migrations are separate, explicit preparation steps. This program never restores, overwrites, migrates, or resets a database.
- Writes fixture data and temporary audit-failure triggers to that database. Trigger creation/drop uses `finally`; verify metadata after interrupted runs. Do not launch simultaneous harness runs on the same database.
- Preserves fixtures and results for investigation; use a new restore for a clean baseline. JSON evidence is written beside the test DB. No production business code or migration is patched by this harness.

## Run

```powershell
dotnet restore tests/Kazuko.MoneyFlow.IntegrationChecks/Kazuko.MoneyFlow.IntegrationChecks.csproj
dotnet build tests/Kazuko.MoneyFlow.IntegrationChecks/Kazuko.MoneyFlow.IntegrationChecks.csproj --no-restore -p:BuildInParallel=false
dotnet tests/Kazuko.MoneyFlow.IntegrationChecks/bin/Debug/net8.0-windows/Kazuko.MoneyFlow.IntegrationChecks.dll D:\Working\artifacts\money-flow-gd3\test91_moneyflow_clean_20261028.fdb
```

`--probe` reads schema/master availability without running fixtures. Filter scenarios with `--case=T08` or multiple prefixes such as `--case=T04/card --case=T09/debt`. No matching cases, unexpected exceptions, or unmet evidence assertions produce a nonzero exit code. `OBSERVED` is recorded per completed scenario; `ERROR` includes diagnostic evidence. Do not count tests with missing fixtures as passed.

Race checks use two independent production service calls released by one gate, and inspect committed effects. Replay checks resend a completed request; they do not simulate an actual TCP break at commit. Product fixtures use test codes and synthetic customers; most cash/debt tests create minimal orders directly, while double-collection/offset/reference tests use actual checkout posting.

Before turning this into a regression suite, replace defect-reproduction assertions with corrected business expectations. Consult the report for remaining untested variants and the distinction between API-only legacy paths and current UI paths.
