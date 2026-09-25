# Project Overview

KAZUKO ERP JEWELRY is a new Windows desktop foundation for a jewelry workshop ERP. The current repository contains the application shell, login flow, dashboard, appearance services, module contracts, and update-package staging. It does not yet contain production CRUD modules or a live Firebird integration.

The approved legacy WinForms application at `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0` is documented elsewhere as a business and UI reference. It is not part of this repository and must not be modified while working on this WPF solution.

## Technology Stack

- .NET 8; the SDK is pinned to `8.0.100` by `global.json`.
- C# with nullable reference types, implicit usings, latest language version, and warnings treated as errors.
- WPF for the executable and UI class libraries.
- XAML `ResourceDictionary` files for colors, typography, and shared control styles.
- A small in-repository MVVM foundation: `ObservableObject`, `RelayCommand`, and `AsyncRelayCommand`.
- A custom singleton `ServiceRegistry` for composition and constructor injection; no external DI package is installed.
- JSON in LocalAppData for appearance preferences.
- ZIP-based `.kazuko-module` package validation/staging with a manifest and SHA-256 entry-assembly check.
- Firebird is the intended production database, but the current repository has no Firebird NuGet provider, connection helper, repository, migration runner, or active database calls.

The solution currently has no third-party NuGet package references.

## Architecture

The solution is split into six projects:

- `Kazuko.App`: WPF executable, startup composition, login-window hosting, shell window, and shell ViewModel.
- `Kazuko.Core`: framework-light contracts and primitives for MVVM, authentication, appearance, module registration, updates, and service registration.
- `Kazuko.Infrastructure`: current implementations for development authentication, JSON preferences, and update-package validation/staging.
- `Kazuko.Shared.UI`: WPF themes, shared styles, typography, and the appearance service.
- `Kazuko.Modules.Authentication`: login View and ViewModel.
- `Kazuko.Modules.Dashboard`: dashboard module registration and dashboard View.

The current dependency direction is broadly:

```text
Kazuko.App -> Core, Infrastructure, Shared.UI, Modules
Modules -> Core, Shared.UI
Infrastructure -> Core
Shared.UI -> Core
Core -> no other solution project
```

`App.xaml.cs` is the composition root. It creates the service registry, registers foundation services, registers modules, applies saved appearance settings, and controls the login-to-shell transition.

MVVM is used partially and intentionally:

- `ShellViewModel` owns shell navigation and appearance selection.
- `LoginViewModel` owns login state and command execution.
- `DashboardView` is currently static and has no ViewModel.
- Code-behind is limited to `InitializeComponent` and synchronizing `PasswordBox.Password` to `LoginViewModel`, because WPF does not expose password binding directly.
- Navigation creates View objects through module-registered factories. There is not yet a separate navigation service or ViewModel-to-View template registry.

There are no current application services, repositories, DAO classes, domain models, production authorization services, or CRUD workflows. Agents must not invent that these layers already exist. Add them only when an explicitly assigned feature requires them and follow the contracts already present in `Kazuko.Core`.

## Important Directories

- `src/Kazuko.App/`: executable host, `App.xaml`, composition root, login host window, shell window, and shell ViewModel.
- `src/Kazuko.Core/`: cross-project contracts and low-level MVVM/service-registry primitives. It must remain independent of WPF and infrastructure implementations.
- `src/Kazuko.Infrastructure/`: file/system service implementations. This is the likely future home of Firebird connection and repository implementations unless an approved architectural decision establishes a dedicated data project.
- `src/Kazuko.Shared.UI/`: global WPF resources and appearance behavior shared by all screens.
- `src/Modules/Kazuko.Modules.Authentication/`: login UI and state.
- `src/Modules/Kazuko.Modules.Dashboard/`: dashboard registration and UI.
- `docs/`: product architecture, UI/accessibility rules, module update design, and development guidance created before this AI workflow.
- `.ai/`: coordination documents, task ownership, task state, and reviews for Codex, Cursor, and Antigravity.

## Database

Firebird is a documented target requirement, not an implemented dependency in the current repository.

Current facts:

- No Firebird package is referenced by any `.csproj`.
- No Firebird connection string, connection factory, helper, repository, DAO, SQL command, transaction helper, or migration is present.
- Authentication currently uses `DevelopmentAuthenticationService` and accepts the local development account described in the README; it does not query Firebird.
- There is therefore no current repository convention, ID-generation strategy, or transaction convention to copy.

For future database tasks:

- First inspect the approved legacy project for business/schema behavior, but do not edit it.
- Put database access behind Core/application-facing interfaces; do not put SQL in Views or ViewModels.
- Use parameterized Firebird SQL and explicit disposal.
- Use a transaction for multi-step writes.
- Document the chosen connection, repository, ID-generation, migration, and transaction patterns here as soon as the first production Firebird slice is approved and implemented.
- Do not add a Firebird package or change schema without explicit task authorization.

## UI

The best current UI references are:

1. `src/Kazuko.App/MainWindow.xaml` for the application shell: left navigation, top header, content host, appearance selectors, spacing, and surface/border use.
2. `src/Modules/Kazuko.Modules.Dashboard/DashboardView.xaml` for page headings, card layout, scroll behavior, wrapping, and semantic resources.
3. `src/Modules/Kazuko.Modules.Authentication/LoginView.xaml` for form labels, `TextBox`, `PasswordBox`, primary action, validation/error text, and card presentation.

There is no existing CRUD/DataGrid screen in the WPF repository, so agents must not claim an established DataGrid convention. The first approved CRUD task should define the smallest reusable DataGrid/edit-form convention and update this document after review.

Current common UI conventions:

- Theme-sensitive values use `DynamicResource`.
- Shared resources live in `Kazuko.Shared.UI/Themes`.
- Semantic font keys are `FontSize.Caption`, `FontSize.Body`, `FontSize.Subtitle`, `FontSize.Heading`, and `FontSize.Display`.
- Semantic brushes include `PrimaryBrush`, `AppBackgroundBrush`, `SurfaceBrush`, `SurfaceAltBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `BorderBrush`, `SuccessBrush`, and `DangerBrush`.
- Existing shared styles include `PrimaryButtonStyle`, `NavButtonStyle`, and `CardBorderStyle`.
- Layout uses `Grid`, `DockPanel`, `StackPanel`, `WrapPanel`, `ItemsControl`, and `ScrollViewer`; absolute positioning is not the current pattern.
- The app supports Light/Dark themes and font scales from 90% through 150%.

## Development Priorities

1. Correct functionality
2. Preserve existing architecture
3. Consistent UI
4. Simple maintainable code
5. Avoid unnecessary abstraction
6. Avoid unrelated changes
