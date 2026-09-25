# TASK-007B1 – MODULE ACTIVATION CATALOG / STATE STORE

## Assigned Agent

Cursor

## Status

DONE

## Goal

Implement Staged → Installed → PendingActivation catalog: atomic `activation-state.json` with backup recovery, cross-process activation lock, filesystem/path/tamper protection when promoting staging directories. No DLL load, no migration, no activation pipeline, no Import UI.

## Context

Based on `.ai/plans/TASK-007B-MODULE-ACTIVATION-ROLLBACK.md` section 007B1. Builds on TASK-007A staging only. Does not start 007B2/B3/B4.

## Requirements completed

- Core contracts: `ModuleActivationState`, `ModuleActivationRecord`/`Document`, `IModuleLayoutPaths`, `IModuleActivationCatalog`, `IModuleActivationLock`/`Factory`
- Infrastructure: `ModuleLayoutPaths`, `ModuleActivationLockFactory`, `ActivationStateStore`, `ModuleActivationCatalog`
- Promote staging → `Installed/{moduleId}/{version}` with payload hash re-verify
- Idempotent same version + same package SHA-256; reject same version different content
- Atomic state write (tmp + replace + `.bak`); corrupt primary recovers from `.bak`
- Exclusive `activation.lock` (`FileShare.None`)
- Reject staging outside StagingRoot; reject reparse points; cleanup failed promote temps
- App composition registers layout + catalog (no startup activation yet)

## Allowed Files

- `src/Kazuko.Core/Updates/ActivationContracts.cs`
- `src/Kazuko.Infrastructure/Updates/Activation/**`
- `tests/Kazuko.Infrastructure.Tests/Updates/Activation/**`
- `src/Kazuko.App/App.xaml.cs` (composition only)
- `.ai/PROJECT.md` (brief status)
- this task note

## Do Not Modify

- Package format v2 / TASK-007A installer behavior
- Database / Supplier migration / TASK-008*
- Module loader (B2), gbak/migration runner (B3), startup pipeline (B4), Import UI (007C)

## Acceptance Criteria

- [x] Staged → Installed → PendingActivation
- [x] Atomic activation-state.json + .bak recovery
- [x] Cross-process lock
- [x] Path/tamper protection tests
- [x] Build 0W/0E, tests pass
- [x] No B2/B3/B4 scope

## Build / Verification

```text
dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release  → 0 Warning / 0 Error
dotnet test  KAZUKO_ERP_JEWELRY.sln --configuration Release --no-build → Passed 45/45
git diff --check → clean
```

## Notes

Activation of PendingActivation modules remains TASK-007B4. Signature re-verify with trusted keys at cold start remains B2/B4.
