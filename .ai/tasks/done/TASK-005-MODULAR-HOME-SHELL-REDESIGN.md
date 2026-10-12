# TASK-005 - Modular Home Shell Redesign

## Assigned Agent

Codex

## Status

DONE

## Goal

Port the approved WinForms home shell to WPF with the supplied logo, icons, and background while preserving theme/font scaling and module-driven navigation.

## Context

The approved UI is in `D:\Working\KAZUKO_ERP_JEWELRY_UI_APPROVED_v1.3.0`. The user requires the sidebar collapse handle to sit at the vertical center of the sidebar/content boundary. Future imported modules must be able to contribute navigation and home tiles through Core contracts without edits to `MainWindow` or `DashboardView`.

## Requirements

- Extend navigation metadata with module-owned icon URI, home description, sidebar/home visibility, and replaceable placeholder support.
- Add a framework-light navigation request service so home tiles can navigate without coupling Dashboard to App.
- Render sidebar items and dashboard tiles from the module registry.
- Preserve appearance settings and expose them through the Settings route.
- Implement custom shell chrome, live clock, logout, collapsed/expanded sidebar, and active navigation state.
- Use only existing repository assets; do not add packages.

## UI Requirements

- Match the approved orange/cream shell at maximized size.
- Expanded sidebar width 330; collapsed width 82.
- Collapse/expand handle is centered vertically on the sidebar's right boundary.
- Dashboard contains logo, title/subtitle, eight module cards, three information blocks, and bottom status bar.
- Use `DynamicResource` for theme-sensitive brushes and semantic font sizes.
- Layout remains usable at supported font scales and smaller window sizes through scrolling where needed.

## Database Requirements

None. Do not modify Firebird code, schema, database files, or authentication behavior.

## Allowed Files

- `src/Kazuko.Core/Modules/ModuleContracts.cs`
- `src/Kazuko.App/App.xaml.cs`
- `src/Kazuko.App/MainWindow.xaml`
- `src/Kazuko.App/MainWindow.xaml.cs`
- `src/Kazuko.App/ShellViewModel.cs`
- `src/Modules/Kazuko.Modules.Dashboard/**`
- `src/Kazuko.Shared.UI/Themes/Light.xaml`
- `src/Kazuko.Shared.UI/Themes/Dark.xaml`
- `docs/01-ARCHITECTURE.md`
- `docs/02-UI-AND-ACCESSIBILITY.md`
- `.ai/PROJECT.md`
- `.ai/tasks/doing/TASK-005-MODULAR-HOME-SHELL-REDESIGN.md`

## Do Not Modify

- Login/authentication files
- Firebird infrastructure and database files
- Shared theme files other than `Light.xaml` and `Dark.xaml`
- Module package installer/update validation

## Acceptance Criteria

- Shell and home page visually match the supplied approved references.
- Sidebar expands/collapses and the handle remains centered at its right boundary.
- All menu items and eight home tiles are generated from registered navigation metadata.
- A non-placeholder module registration can replace the matching placeholder route safely.
- Home cards navigate through the navigation service; Shell/Dashboard do not switch on feature keys.
- Theme/font preferences remain functional from Settings.
- Logout returns to Login.
- Solution builds with zero warnings and errors.

## Build / Verification

```powershell
dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Debug
```

Manual checks: expanded/collapsed sidebar, each home tile, settings appearance controls, logout/login cycle, light/dark themes, font scales 90/100/125/150%, maximized and minimum window size.

## Notes

Existing dirty changes in Login and Firebird files belong to earlier completed tasks and are explicitly outside this task.

Implementation completed by Codex on 2026-09-25.

- Added module-driven sidebar/home metadata, key-based navigation service, and safe placeholder replacement.
- Rebuilt the WPF shell and Dashboard from the approved WinForms reference using repository assets.
- Added appearance settings route, placeholder feature views, live clock, custom window chrome, logout, and centered sidebar boundary toggle.
- Updated architecture/UI documentation.
- `dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release`: succeeded, 0 warnings / 0 errors.
- `dotnet test KAZUKO_ERP_JEWELRY.sln --configuration Release --no-build`: succeeded (no test projects currently present).
- Debug output verification was blocked only by the already-running `KAZUKO_ERP_JEWELRY` process locking its DLLs; Release verification is clean.
- Native-window screenshot QA was unavailable in the current automation surface; user visual confirmation remains recommended.

### Follow-up fix (TASK-005a, 2026-09-25)

Runtime `InvalidOperationException` on login → MainWindow: `Run.Text` TwoWay binding to read-only `DisplayName`. Fixed with `Mode=OneWay` on read-only shell/dashboard bindings (see `.ai/tasks/done/TASK-005a-FIX-READONLY-BINDING-MODE.md`). Release rebuild: 0 warning / 0 error.
