# TASK-005a - Fix Read-Only Binding Mode on Shell XAML

## Assigned Agent

Cursor

## Status

DONE

## Goal

Fix runtime `InvalidOperationException` when opening MainWindow after login: TwoWay/OneWayToSource binding on read-only `ShellViewModel.DisplayName` via `Run.Text`.

## Cause

In `MainWindow.xaml`, `<Run Text="{Binding DisplayName}" />` used the default binding mode for `Run.Text`, which is TwoWay. `DisplayName` is get-only (`public string DisplayName { get; }`), so WPF throws at runtime.

## Fix

Set `Mode=OneWay` on bindings to read-only shell/dashboard properties. Did not add fake setters to ViewModels.

## Files changed

- `src/Kazuko.App/MainWindow.xaml`
  - `DisplayName` on `Run.Text` → `Mode=OneWay` (root cause)
  - Also OneWay: `SidebarWidth`, `SidebarToggleGlyph`, `SidebarToggleToolTip`, `DateText`, `TimeText`, `CurrentView`, `Item.Label` / `Item.IconUri` / `Item.IconGlyph`
- `src/Modules/Kazuko.Modules.Dashboard/DashboardView.xaml`
  - OneWay: tile `Label` / `IconUri` / `Description`, `VersionText`, `UpdatedText`
- `src/Modules/Kazuko.Modules.Dashboard/ModulePlaceholderView.xaml`
  - OneWay: `IconUri`, `Label`, `Description`

## Not changed

- Firebird, Login, database, ViewModel setters, shell design/layout, module architecture

## Build

```powershell
dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Release
```

Result: **succeeded**, 0 warning(s), 0 error(s). No running `KAZUKO_ERP_JEWELRY` process was locking DLLs at build time.

## Runtime checklist (operator)

1. Set `KAZUKO_FIREBIRD_PASSWORD` and sign in successfully.
2. MainWindow opens without BindingException.
3. Collapse/expand sidebar works.
4. Home shows logo, background, 8 cards.
5. Settings opens.
6. Logout returns to Login.
