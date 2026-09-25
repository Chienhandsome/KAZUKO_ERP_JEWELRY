# TASK-007B1 – MODULE ACTIVATION CATALOG / STATE STORE

## Assigned Agent

Cursor

## Status

DONE

## Goal

Implement Staged → Installed → PendingActivation catalog with fail-closed state store, atomic `activation-state.json`, cross-process lock (mutex + file), fingerprint/tamper protection, reserved host modules, and pending-conflict rules. No DLL load, no migration, no activation pipeline, no Import UI.

## Hardening (post-initial) invariants

- State fail-closed: empty only when both primary+backup missing; corrupt without valid backup → `STATE_CORRUPT`; unsupported schema → `STATE_SCHEMA_UNSUPPORTED`; recover from backup without clobbering good `.bak`
- Strict JSON: disallow unmapped members, case-sensitive properties/enums, reject bad queue/module IDs
- Public API: Load / Get / Accept only — no `SaveAsync` state-machine bypass
- Revision + installed version catalog + fingerprint (module.json ‖ signature.sig) + InstalledAtUtc; deterministic serialize order
- Pending conflicts: idempotent same version+fingerprint; reject different fingerprint or different pending version
- Reserved deny-by-default: Dashboard, Authentication, Customers
- Exact staged/installed contents; reparse chain checks; async cancellable copy; rollback install if state commit fails after rename; staging deleted only after commit
- Named mutex (`Local\KazukoERP.ModuleActivation.*`) + exclusive file lock; abandoned → fail closed

## Build / Verification

```text
dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release  → 0 Warning / 0 Error
dotnet test  KAZUKO_ERP_JEWELRY.sln --configuration Release → Passed 62/62
git diff --check → clean
```

## Notes

B2/B3/B4 not started. Internal transitions for Active/Activating/etc. deferred to those tasks.
