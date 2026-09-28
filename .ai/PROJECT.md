# Project Overview

KAZUKO ERP JEWELRY is a Windows desktop foundation for a jewelry workshop ERP. The repository contains the application shell, Firebird-backed login, dashboard with modular navigation placeholders, appearance services, module contracts, update-package staging, and the first real CRUD module: Customers (`Kazuko.Modules.Customers`).

The approved legacy WinForms application at `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0` is documented elsewhere as a business and UI reference. It is not part of this repository and must not be modified while working on this WPF solution.

## Technology Stack

- .NET 8; the SDK is pinned to `8.0.100` by `global.json`.
- C# with nullable reference types, implicit usings, latest language version, and warnings treated as errors.
- WPF for the executable and UI class libraries.
- XAML `ResourceDictionary` files for colors, typography, and shared control styles.
- A small in-repository MVVM foundation: `ObservableObject`, `RelayCommand`, and `AsyncRelayCommand`.
- A custom singleton `ServiceRegistry` for composition and constructor injection; no external DI package is installed.
- JSON in LocalAppData for appearance preferences.
- ZIP-based `.kazuko-module` **secure validation/staging** (TASK-007A) plus **activation catalog** (TASK-007B1, hardened): promote Staging → `Installed/{moduleId}/{version}`, fail-closed atomic `activation-state.json`, mutex+file lock, package fingerprint, PendingActivation queue. Still no cold-start activation, migration, or Import UI (007B2–B4 / 007C).
- Firebird Embedded via `FirebirdSql.Data.FirebirdClient` 10.3.4 (Infrastructure only).
- Test project: `tests/Kazuko.Infrastructure.Tests` (xUnit) for package security and activation catalog cases.

## Architecture

The solution projects:

- `Kazuko.App`: WPF executable, startup composition, login-window hosting, shell window, and shell ViewModel.
- `Kazuko.Core`: framework-light contracts (MVVM, authentication/session, appearance, modules, updates, Customers contracts, Suppliers contracts, Warehouse contracts).
- `Kazuko.Infrastructure`: Firebird connection/auth/repositories, preferences, update staging.
- `Kazuko.Shared.UI`: WPF themes, shared styles, typography, appearance service.
- `Kazuko.Modules.Authentication`: login View and ViewModel.
- `Kazuko.Modules.Dashboard`: dashboard + placeholder routes for unfinished modules.
- `Kazuko.Modules.Customers`: real KHÁCH HÀNG vertical slice (list/search/create/edit/activate) and `ICustomerEditorService`.
- `Kazuko.Modules.Suppliers`: real NHÀ CUNG CẤP vertical slice (list/search/create/edit/activate) and `ISupplierEditorService`.
- `Kazuko.Modules.Warehouse`: read-only KHO vertical slice (material balances, stock documents/detail, movement ledger).

Dependency direction:

```text
Kazuko.App -> Core, Infrastructure, Shared.UI, Modules
Modules -> Core, Shared.UI
Infrastructure -> Core
Shared.UI -> Core
Core -> no other solution project
```

`App.xaml.cs` is the composition root. It registers foundation services (including `ICurrentSessionAccessor` and `ICustomerRepository`), registers modules (Dashboard then Customers so the real `CUSTOMER` route replaces the placeholder), applies appearance, and controls login-to-shell with session set/clear.

## Database

Firebird Embedded 5.0.4 conventions (TASK-001 + TASK-006 + TASK-008A):

- Provider only in Infrastructure; reuse `FirebirdDatabase.CreateConnection()`.
- Development DB: `data\data.fdb` (gitignored). Password from `KAZUKO_FIREBIRD_PASSWORD`.
- Migrations: official scripts tracked in `database/migrations/` and recorded in `SCHEMA_MIGRATIONS` with SHA-256 checksums.
- Customer persistence: `FirebirdCustomerRepository` against `CUSTOMERS` / `CUSTOMER_GROUPS` / `SALES_ORDERS` / `ORDER_PAYMENTS` / `AUDIT_LOG`.
- Supplier persistence: `FirebirdSupplierRepository` against `SUPPLIERS` / `SUPPLIER_GROUPS` / `STOCK_DOCUMENTS` / `AUDIT_LOG` (TASK-008A + TASK-008B).
- SQL stays in Infrastructure. Views/ViewModels must not contain SQL.

## Session

- `AuthenticatedSession` carries UserId, Username, DisplayName, RoleCode, BranchId/Code/Name (never password).
- `ICurrentSessionAccessor` singleton stores the current session after login and clears on logout.
- `FirebirdAuthenticationService` returns `AuthenticationResult.Session` on success.

## UI

References:

1. `MainWindow.xaml` — application shell.
2. `DashboardView.xaml` — home tiles from navigation registry.
3. `LoginView.xaml` — forms and validation presentation.
4. `CustomerView.xaml` / `CustomerEditDialog.xaml` — first CRUD + local DataGrid convention (see `docs/02-UI-AND-ACCESSIBILITY.md`).

DataGrid styles stay module-local for Customers; do not invent a global DataGrid dictionary until a later promote task.

Cross-module customer create/edit: resolve `ICustomerEditorService` from Core; do not `new` the dialog from other modules.

## Module packages

- Format: `.kazuko-module` ZIP; `module.json` + `signature.sig` + declared payload only.
- Signature: RSA-PSS-SHA256 over raw `module.json` bytes; public keys from `ITrustedPublicKeyStore` (empty by default until provisioned).
- Private signing keys must never enter this repository.
- Default composition constructs `ModulePackageInstaller` against `IModuleLayoutPaths.StagingRoot`, plus `IModuleActivationCatalog` for Staged→Installed→PendingActivation (no DLL load yet).
- Default composition still uses host version 1 and empty key store for package validation — packages fail closed without trusted keys.

## Development Priorities

1. Correct functionality
2. Preserve existing architecture
3. Consistent UI
4. Simple maintainable code
5. Avoid unnecessary abstraction
6. Avoid unrelated changes
