# TASK-002 - Embedded Firebird Operations Guide

## Assigned Agent

Antigravity

## Status

DONE

## Goal

Create a concise, evidence-based guide for local development and future customer deployment of Firebird Embedded 5.0.4, without changing application behavior, source code, packages, or any database file.

## Context

`TASK-001` is actively implementing Firebird-backed login. The user-provisioned Firebird Embedded runtime is located at:

```text
D:\Working\KAZUKO_ERP_JEWELRY\firebird\Firebird-5.0.4.1812-0-windows-x64\
```

The approved legacy WinForms application is read-only reference material at:

```text
D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0
```

This task is intentionally documentation-only and must not overlap the active Cursor task.

## Requirements

1. Read `.ai/PROJECT.md`, `.ai/RULES.md`, `AGENTS.md`, and this task before working.
2. Inspect the supplied runtime folder and the legacy Firebird build/runtime configuration read-only.
3. Create `docs/05-EMBEDDED-FIREBIRD-OPERATIONS.md` in Vietnamese.
4. Clearly distinguish:
   - facts already implemented in the WPF repository;
   - runtime/database files supplied manually for development;
   - future installer/deployment recommendations that are not yet implemented.
5. Explain the required embedded runtime contents, expected development folder layout, development database-copy rule, and release folder/data-folder recommendation.
6. State that Embedded is for local/single-host access and a Firebird Server is required for simultaneous multi-machine access to a shared database.
7. Include a short troubleshooting checklist: missing `fbclient.dll`, wrong x64/x86 architecture, database file access/permissions, and database placed under Program Files.
8. Do not include passwords, connection secrets, or machine-specific production customer paths.

## UI Requirements

None.

## Database Requirements

- Do not open, copy, modify, repair, migrate, or query any `.fdb` file.
- Do not create database schema documentation beyond operational placement guidance.

## Allowed Files

- `.ai/tasks/doing/TASK-002-EMBEDDED-FIREBIRD-OPERATIONS-GUIDE.md` (move/update task state and notes only)
- `.ai/tasks/done/TASK-002-EMBEDDED-FIREBIRD-OPERATIONS-GUIDE.md` (moved here upon completion)
- `docs/05-EMBEDDED-FIREBIRD-OPERATIONS.md`

## Do Not Modify

- Any file owned by TASK-001, including `.gitignore`, `src/Kazuko.Infrastructure/**`, `src/Kazuko.App/**`, and Login module files.
- Any `.cs`, `.xaml`, `.csproj`, `.sln`, `.props`, package, runtime binary, `.fdb`, migration, or legacy WinForms file.
- Existing `docs/00-*` through `docs/04-*` and `.ai/PROJECT.md`.

## Acceptance Criteria

- The new guide is in Vietnamese and based on actual local/legacy inspection.
- It does not claim that WPF Firebird integration or an installer is already complete.
- It covers runtime contents, layouts, multi-user limitation, and troubleshooting.
- No application source, package, schema, runtime binary, or database file changes are made.
- Task Notes list the read-only files/folders inspected and review fixes addressed.

## Build / Verification

No build is required because this is documentation-only.

Verify that only the Allowed Files changed and that the Markdown renders as readable headings, lists, and code blocks.

## Notes

- Ownership overlap check: TASK-001 currently owns no file in `docs/05-EMBEDDED-FIREBIRD-OPERATIONS.md`; this task has no approved overlap.
- Inspected read-only local directories and files:
  - `firebird/Firebird-5.0.4.1812-0-windows-x64/` (`fbclient.dll`, `firebird.conf`, `plugins.conf`, `databases.conf`, `firebird.msg`, `gbak.exe`, `icudt63l.dat`, ICU dlls, VC runtime dlls)
  - `firebird/Firebird-5.0.4.1812-0-windows-x64/plugins/` (`engine13.dll`, `srp.dll`, `legacy_auth.dll`, `legacy_usermanager.dll`, `chacha.dll`, `udr_engine.dll`, etc.)
  - `firebird/Firebird-5.0.4.1812-0-windows-x64/intl/` (`fbintl.dll`, `fbintl.conf`)
  - `firebird/Firebird-5.0.4.1812-0-windows-x64/tzdata/` (`zoneinfo64.res`, `windowsZones.res`, `metaZones.res`, `timezoneTypes.res`, `ids.dat`)
  - `data/` folder directory listing (confirmed `data.fdb` exists; no read/copy/touch performed)
  - `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0\publish-embedded.bat` (published with `win-x64`, `--self-contained true`)
  - `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0\src\KazukoErp.Desktop\KazukoErp.Desktop.csproj` (shows Firebird runtime items linking into `firebird/`, `PreserveNewest`, `data/data.fdb` linking into `data/data.fdb`)
  - `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0\src\KazukoErp.Desktop\Data\FirebirdDatabase.cs` (shows `FbServerType.Embedded`, `ClientLibrary = FirebirdRuntime.ClientLibraryPath`)
  - `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0\src\KazukoErp.Desktop\Data\FirebirdRuntime.cs` (shows resolution of `fbclient.dll` and tools like `gbak.exe`)
- Result: Created `docs/05-EMBEDDED-FIREBIRD-OPERATIONS.md`.
- Review fixes completed (addressed all items from `.ai/reviews/REVIEW-TASK-002-EMBEDDED-FIREBIRD-OPERATIONS-GUIDE.md`):
  1. Corrected executable name to `KAZUKO_ERP_JEWELRY.exe` and Debug output path to `src\Kazuko.App\bin\Debug\net8.0-windows\`.
  2. Made TASK-001 status time-safe by directing readers to check `.ai/tasks/` for current status rather than assuming static state.
  3. Replaced individual "minimum file" prescription with the complete-bundle rule while retaining examples of commonly required components.
  4. Updated `ProgramData` permission advice to least-privilege write access for authorized accounts/service accounts instead of broad user permissions.
  5. Framed VC++ redistributable requirements as conditional checks based on bundle contents and machine state.
  6. Added note clarifying that target production layout is an architectural reference layout because an installer tool has not yet been selected/implemented.
