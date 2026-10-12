# TASK-009 — Warehouse vertical slices

Status: PLANNED (009A DONE)  
Implementation-ready: YES for TASK-009B–009C  
Priority: Business functionality before module-update hardening

## 1. Objective

Port the Warehouse domain from the approved WinForms application to WPF in small, usable vertical slices. The first release must let a user see raw-material balances, create a supplier receipt, post it atomically, and immediately see the updated balance and movement ledger.

Do not attempt to deliver every screen in `kho.png`, `nhap kho thanh pham.png`, and `xuat kho thanh pham.png` in one task.

## 2. Evidence inspected

### Current WPF repository

- `DashboardModule` currently registers `WAREHOUSE` as a placeholder.
- There is no `Kazuko.Modules.Warehouse`, Warehouse Core contract, or WPF Warehouse repository.
- `App.xaml.cs` is the composition root and currently registers Dashboard, Customers, and Suppliers.
- Customers/Suppliers demonstrate the intended project split, MVVM pattern, module registration, Firebird repository, session access, dialog service, theme resources, and local DataGrid conventions.
- Supplier migration `0030__supplier_core.sql` adds nullable `STOCK_DOCUMENTS.SUPPLIER_ID` with a same-company composite foreign key.
- The repository is clean at planning time.
- Commit `bca6c0c` is named `feat: add KHO`, but its contents implement Suppliers, not Warehouse. Do not use the commit title as evidence that Warehouse exists.

### Approved WinForms reference

Read-only source: `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0`.

Relevant files:

- `Controls/WarehouseManagementControl.cs`
- `Data/WarehouseRepository.cs`
- `Data/WarehouseWorkflowVerifier.cs`
- `Forms/WarehouseForms.cs`
- `Models/InventoryRecords.cs`
- `Models/WarehouseFinishedProductModels.cs`
- `Data/FirebirdDatabaseMigrator.cs`

Verified legacy capabilities:

- Raw-material balance search by text/type/location/low-stock.
- Stock documents: `OPENING`, `RECEIPT`, `ISSUE`, `TRANSFER`.
- Document states: `DRAFT`, `POSTED`, `CANCELLED`.
- Save/edit/delete draft.
- Post document atomically and create immutable stock movements.
- Block negative stock.
- Cancel a posted document by creating reversal movements.
- Moving-average cost for material balances.
- Finished products, stocktake, metrics, reports, and exports also exist, but are later slices.

### UI references

- `src/Assets/examples/kho.png`: visual target for the Warehouse shell/overview.
- `src/Assets/examples/nhap kho thanh pham.png`: later finished-product receipt reference.
- `src/Assets/examples/xuat kho thanh pham.png`: later finished-product issue reference.
- `src/Assets/icons/KHO.png`: Warehouse module icon.

The first MVP follows the current WPF shell rather than duplicating the old screenshot's title bar/sidebar.

## 3. Existing schema relevant to the MVP

No new schema migration is required for TASK-009A–009C if the actual development database matches the approved schema plus migration 0030. Cursor must verify metadata read-only before implementation and stop if required columns/constraints are missing.

Tables:

- `LOCATIONS`: branch-scoped active `WAREHOUSE`/`COUNTER` locations.
- `MATERIALS`: company-scoped material master; types `GOLD`, `STONE`, `ACCESSORY`; unit, purity, reference cost, tracking mode, minimum stock.
- `STOCK_BALANCES`: current quantity and average cost by material/location.
- `STOCK_DOCUMENTS`: header with company, branch, type, locations, supplier, status, posting/cancellation metadata.
- `STOCK_DOCUMENT_LINES`: material, quantity, unit cost, amount, lot, notes.
- `STOCK_MOVEMENTS`: append-only in/out/reversal ledger tied to document and line.
- `SUPPLIERS`: already owned by the Supplier module; Warehouse may consume `ISupplierEditorService` but must not reference the Supplier WPF project.
- `AUDIT_LOG`: important action audit.

Important constraints:

- Document type is one of `OPENING`, `RECEIPT`, `ISSUE`, `TRANSFER`.
- Status is one of `DRAFT`, `POSTED`, `CANCELLED`.
- Balance quantity cannot be negative.
- Stock line quantity must be positive; unit cost and amount cannot be negative.
- Supplier relation must stay within the same company.

## 4. Architecture decisions

### Project split

- `Kazuko.Core/Warehouse`: WPF-neutral records, requests, interfaces, error/result types.
- `Kazuko.Infrastructure/Warehouse`: Firebird SQL and transaction implementation.
- `Modules/Kazuko.Modules.Warehouse`: WPF Views/ViewModels/module registration.
- `Kazuko.App`: composition only.
- `tests/Kazuko.Infrastructure.Tests/Warehouse`: repository integration tests using isolated database copies.

### Dependencies

- Warehouse module references Core + Shared.UI only.
- Warehouse never references `Kazuko.Modules.Suppliers` or `Kazuko.Modules.Customers`.
- Cross-module supplier creation/editing is through `ISupplierEditorService` from Core.
- Supplier lookup data should be exposed through a Core contract/repository already owned by Suppliers, or through a narrowly scoped WPF-neutral selector contract. Do not query Suppliers directly from a Warehouse ViewModel.

### Transaction ownership

`IWarehouseService` is the application-service boundary for write workflows. It owns the transaction semantics through a transaction-capable Infrastructure implementation; ViewModels never coordinate multiple SQL calls.

Posting a document must atomically:

1. Lock and validate the draft header.
2. Load and validate non-empty lines.
3. Validate source/destination locations belong to the authenticated branch.
4. Validate every material belongs to company `KAZUKO` and is active when appropriate.
5. Apply all balance changes.
6. Calculate/update moving-average cost for inbound stock.
7. Reject any outbound operation that would make balance negative.
8. Insert movement ledger rows.
9. Mark the document `POSTED` with user/time.
10. Commit once.

Any failure rolls back header state, balances, and movements together. Audit is written only after commit and audit failure must not hide a successful business result.

### Scope and data isolation

- Every header/read/write is restricted to company code `KAZUKO` and the authenticated branch.
- Never load, edit, post, delete, cancel, or enumerate another branch's document by ID.
- IDs supplied by UI are untrusted.
- Use parameterized SQL only.
- Use `WITH LOCK` or an equivalent Firebird concurrency guard for posting/cancellation.

### Navigation

`Kazuko.Modules.Warehouse` registers real route key `WAREHOUSE` and replaces the Dashboard placeholder through the existing registry behavior. Do not add Warehouse-specific switches to `MainWindow` or `DashboardView`.

### UI and accessibility

- Follow `docs/02-UI-AND-ACCESSIBILITY.md`.
- Use only semantic `DynamicResource` brushes, typography, and styles.
- Support Light/Dark and font scale 90%–150%.
- Use the current shell; do not recreate the reference screenshot's sidebar/title bar inside the module.
- Target the Warehouse content area/layout from `kho.png`.
- Avoid charts in the first slice; KPI and chart accuracy depends on completed workflows.
- No fixed Canvas positioning.

## 5. MVP boundaries

### Included in the first milestone

- Raw-material balances.
- Stock document list/detail.
- Supplier receipt (`RECEIPT`) draft creation/editing.
- Post receipt.
- Movement ledger.
- Supplier selection and quick-create via the existing Supplier contract.

### Explicitly deferred

- Finished-product receipt/issue and photo/barcode workflow.
- Sales-counter stock.
- Stocktake.
- Reports/CSV/printing.
- Opening balance, issue, transfer, posted-document cancellation until TASK-009D.
- Column-profile customization.
- Charts.
- Module package activation/import work.
- Any new schema inferred from screenshots.

## 6. Task breakdown for Cursor

### TASK-009A — Warehouse contracts and read-only inventory slice

Goal: make the real `WAREHOUSE` route open a useful WPF screen showing raw-material balances and stock documents from Firebird.

Deliverables:

1. Add WPF-neutral contracts under `Kazuko.Core.Warehouse`:
   - location option;
   - material option/balance;
   - document summary/detail/line;
   - movement item;
   - search filters;
   - repository read interfaces.
2. Add `FirebirdWarehouseRepository` read methods:
   - locations for current branch;
   - material balances by search/type/location/low-stock;
   - stock documents by search/type/status/date range;
   - document detail and lines;
   - movement list by material/location.
3. Add `Kazuko.Modules.Warehouse` and register the real `WAREHOUSE` route.
4. UI:
   - header and tab/navigation skeleton inspired by `kho.png`;
   - KPI row may show only values that are directly and correctly computed;
   - raw-material balance grid;
   - document grid and read-only detail;
   - movement grid;
   - loading, empty, and safe error states.
5. Register repository/project/module in App and solution.
6. Add repository tests for company/branch isolation and search/filter behavior.

Acceptance:

- Dashboard/sidebar Warehouse opens the real view, not placeholder.
- No write operation exists yet.
- No schema or `data.fdb` mutation.
- Build 0 warnings/errors; all tests pass.
- One focused commit.

### TASK-009B — Supplier receipt draft editor

Goal: create/edit/delete a `RECEIPT` draft with material lines and supplier.

Deliverables:

1. Add save request/result contracts and write service/repository boundary.
2. Add a reusable receipt editor dialog:
   - date;
   - destination warehouse;
   - supplier;
   - reference number;
   - notes;
   - material line picker;
   - quantity, unit cost, lot, notes;
   - totals.
3. Supplier integration:
   - choose an existing active supplier;
   - open quick-create using `ISupplierEditorService`;
   - refresh/select the newly created supplier.
4. Save header + all lines in one transaction.
5. Allow editing/deleting only `DRAFT` documents belonging to current company/branch.
6. Validate destination location, supplier company, active material, positive quantity, non-negative cost, duplicate material/lot policy, and non-empty lines.
7. Audit create/update/delete after commit.
8. Repository tests for rollback and isolation.

Acceptance:

- User can create a supplier receipt draft and reopen/edit it.
- No stock balance or movement changes before posting.
- Delete draft removes header and cascade lines only.
- Build/tests clean; one focused commit.

### TASK-009C — Post receipt and inventory ledger

Goal: turn a receipt draft into posted inventory atomically.

Deliverables:

1. Implement `PostReceiptAsync` with Firebird transaction/concurrency guard.
2. For each line:
   - upsert balance at destination;
   - calculate moving-average cost;
   - insert `IN` movement;
   - preserve line amount/cost.
3. Mark header `POSTED`; reject double posting and cross-branch access.
4. Audit only after commit.
5. Add confirmation UI and refresh balances/documents/movements after success.
6. Integration tests:
   - successful multi-line posting;
   - exact quantity/value/average cost;
   - no partial writes when one line fails;
   - concurrent/double-post prevention;
   - wrong company/branch rejected;
   - audit failure does not undo committed inventory.

Acceptance:

- The complete visible flow works: create draft → post → balance increases → ledger row appears.
- Posted document is read-only.
- Build/tests clean; one focused commit.

### TASK-009D — Issue, transfer, opening balance, and reversal cancellation

Start only after 009C is accepted.

- Add `ISSUE`, `TRANSFER`, and `OPENING` editor rules.
- Block negative stock atomically.
- Transfer posts OUT and IN at the same moving-average cost.
- Cancel posted document with reversal movements; never delete history.
- Add exhaustive transaction/concurrency tests.

### TASK-009E — Finished products

Start only after 009D is stable.

- Implement finished-product inventory from the approved schema and the two image references.
- Do not force finished products into material `STOCK_DOCUMENT_LINES` unless schema evidence supports it.
- Photo/barcode/printing are separate acceptance items.

### TASK-009F — Stocktake, overview KPIs, charts, and reports

Start last.

- Stocktake draft/count/post/cancel.
- Overview KPIs and recent history.
- Low-stock alerts.
- Charts and exports.
- Visual polish against `kho.png` after the underlying numbers are real.

## 7. Required verification per task

1. Read `AGENTS.md` and all required docs before coding.
2. Check clean working tree and preserve unrelated changes.
3. Inspect actual Firebird metadata read-only; never assume a screenshot field exists.
4. Use a copied test database for integration tests; never mutate `data/data.fdb` during tests.
5. `dotnet build KAZUKO_ERP_JEWELRY.sln -c Release`.
6. `dotnet test KAZUKO_ERP_JEWELRY.sln -c Release --no-build`.
7. `git diff --check`.
8. Manual UI matrix: Light/Dark; 90%, 100%, 125%, 150%; minimum window and maximized.
9. Commit only the current task and report SHA, changed files, tests, schema/data impact, and remaining scope.

## 8. Stop conditions

Cursor must stop and report evidence instead of inventing behavior if:

- required table/column/constraint differs from the actual DB;
- posting rules conflict with approved WinForms behavior;
- a schema migration becomes necessary;
- Supplier quick-create cannot be consumed without a module-to-module reference;
- a requested screenshot field has no authoritative persistence mapping.

