# TASK-006 - Customer Vertical Slice

## Assigned Agent

Cursor

## Status

DONE

## Goal

Implement the real WPF + Firebird KHÁCH HÀNG vertical slice: list/search/filter, create/edit dialog (reusable via `ICustomerEditorService`), activate/deactivate, session context for audit/branch, replacing the `CUSTOMER` placeholder route.

## Context

WinForms reference (read-only): `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0`  
UI reference: `src/Assets/examples/Khach hang.png`

## Closure review (final)

Hardening before close (no new business scope):

1. Kept polished `CustomerView.xaml` + `Dialogs/CustomerEditDialog.xaml` (compact DataGrid rows, rounded TextBox/ComboBox/Button templates).
2. All customer SELECT/UPDATE by ID scoped with `COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')`.
3. `EnsureGroupIsActiveAsync` also requires group `COMPANY_ID` = KAZUKO.
4. `SaveAsync` rolls back only when the transaction has not yet committed (`committed` flag).

## Build / Verification

```powershell
dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release
dotnet test KAZUKO_ERP_JEWELRY.sln --configuration Release --no-build
```

### Results (closure)

- **Build Release:** 0 Warning(s), 0 Error(s) — succeeded.
- **Test Release --no-build:** exit 0 (no test projects in solution).
- **Schema / `data.fdb`:** unchanged; not modified in this task.

### Scenarios covered by implementation review

| Scenario | Status |
|---|---|
| Login returns `AuthenticatedSession`; App stores/clears via `ICurrentSessionAccessor` | Code path verified |
| `CUSTOMER` real route replaces Dashboard placeholder via `ModuleNavigationRegistry` | Code path verified |
| List/search/filter uses one aggregate query (sales + debt), COMPANY scoped | Code path verified |
| Create/Edit via `ICustomerEditorService` / shared dialog; Cancel → null | Code path verified |
| Archive/reactivate updates `IS_ACTIVE` + audit; COMPANY scoped | Code path verified |
| Duplicate phone/tax warning before save | Code path verified |
| Validation (name, phone, group, points, credit, email) | Code path verified |
| UI fidelity polish: shorter grid rows, rounded controls | XAML committed in closure |
| No SQL in View/ViewModel; module does not reference Infrastructure | Verified |
| `.kazuko-module` production installer | Explicitly **not** claimed complete |

### Placeholders remaining (by design)

- Tabs besides “Danh sách khách hàng”
- Bottom cards: Ảnh giấy tờ / Lịch sử mua hàng / Công nợ
- Excel import/export, row “Xem”, avatar/CCCD images

## Notes

Schema unchanged. No writes to WinForms reference DB. Closure commit excludes `bin/`/`obj/`/`data.fdb`.
