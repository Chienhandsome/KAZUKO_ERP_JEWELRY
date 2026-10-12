# TASK-003 - Login Window Redesign

## Assigned Agent

Codex

## Status

DONE

## Goal

Redesign the Firebird login window to match the supplied two-panel Kazuko reference while preserving the existing authentication behavior.

## Allowed Files

- `src/Kazuko.App/LoginWindow.xaml`
- `src/Kazuko.App/LoginWindow.xaml.cs`
- `src/Modules/Kazuko.Modules.Authentication/Kazuko.Modules.Authentication.csproj`
- `src/Modules/Kazuko.Modules.Authentication/LoginView.xaml`
- `src/Modules/Kazuko.Modules.Authentication/LoginView.xaml.cs`
- `src/Modules/Kazuko.Modules.Authentication/LoginViewModel.cs`
- `.ai/tasks/doing/TASK-003-LOGIN-WINDOW-REDESIGN.md`

## Do Not Modify

- Firebird authentication, database, schema, runtime, shared theme dictionaries, AppShell, or other modules.

## Acceptance Criteria

- Login window opens centered at 50% work-area width and 60% work-area height.
- User cannot resize or maximize the login window.
- Logo uses `src/Assets/icons/logo.png`.
- Layout has the requested orange brand panel and white form panel.
- Existing branch selection, username/password login, and error binding remain functional.
- Build succeeds without warnings/errors.

## Notes

- Implemented the two-panel orange/white Login design using `src/Assets/icons/logo.png` as a WPF resource.
- `LoginWindow` calculates 50% width and 60% height from the Windows work area once on load, centers itself, and locks min/max dimensions with `ResizeMode="NoResize"`.
- Existing Firebird authentication, branch selection, error binding, and password entry remain intact. The password visibility button is view-only behavior in code-behind.
- `dotnet build KAZUKO_ERP_JEWELRY.sln --configuration Debug` succeeded with 0 warnings and 0 errors.
