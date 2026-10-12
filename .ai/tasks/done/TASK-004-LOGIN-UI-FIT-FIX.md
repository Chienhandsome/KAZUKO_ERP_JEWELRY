# TASK-004 - Login UI Fit and Controls Fix

## Assigned Agent

Cursor

## Status

DONE

## Goal

Fix the Login window so the full form (including ĐĂNG NHẬP and Firebird status) always fits inside the fixed 50%×60% work-area window without a vertical scrollbar, and style ComboBox/TextBox/PasswordBox to match the approved orange/white login screenshot.

## Allowed Files

- `src/Kazuko.App/LoginWindow.xaml`
- `src/Kazuko.App/LoginWindow.xaml.cs`
- `src/Modules/Kazuko.Modules.Authentication/LoginView.xaml`
- `src/Modules/Kazuko.Modules.Authentication/LoginView.xaml.cs`
- `src/Modules/Kazuko.Modules.Authentication/LoginViewModel.cs`
- `src/Modules/Kazuko.Modules.Authentication/Kazuko.Modules.Authentication.csproj`
- `.ai/tasks/doing/TASK-004-LOGIN-UI-FIT-FIX.md` (moved to done)
- `.ai/reviews/REVIEW-TASK-004-LOGIN-UI-FIT-FIX.md`

## Do Not Modify

- Firebird authentication, connection profile, schema, database, shared themes, AppShell, Dashboard

## Notes

- Removed right-panel `ScrollViewer`; form uses Grid + `Viewbox StretchDirection=DownOnly` (design canvas 980×560).
- Panel split ~57% / 43%; logo from `src/Assets/icons/logo.png`.
- Custom ComboBox/TextBox/PasswordBox: white fill, light-orange border, corner radius 10.
- Fixed error row (~2 lines) so ĐĂNG NHẬP and Firebird status stay visible.
- `IsLoadingBranches` disables inputs/button and shows loading caption; password Hiện/Ẩn uses PasswordBox/TextBox toggle.
- Missing config maps to: `Chưa cấu hình kết nối Firebird. Liên hệ quản trị viên.`
- Window remains 50%×60% work area, centered, `ResizeMode=NoResize`.
- Auth/Infrastructure untouched. Valid login verified with local `KAZUKO_FIREBIRD_PASSWORD`.
- `dotnet build KAZUKO_ERP_JEWELRY.sln`: 0 warning / 0 error.
