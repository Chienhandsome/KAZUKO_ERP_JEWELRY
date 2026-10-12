# REVIEW - TASK-004 Login UI Fit and Controls Fix

## Summary

Login form no longer uses a right-panel `ScrollViewer`. Content is laid out in a fixed design grid inside a `Viewbox` (`StretchDirection=DownOnly`) so ĐĂNG NHẬP, remember-checkbox, error text, and Firebird status remain visible in the fixed 50%×60% NoResize window. ComboBox/TextBox/PasswordBox use local rounded white templates with light-orange borders. Missing `KAZUKO_FIREBIRD_PASSWORD` maps to a compact Vietnamese message without exposing path/SQL/stack.

## Verification

- `dotnet build KAZUKO_ERP_JEWELRY.sln`: 0 warning / 0 error (after releasing a locked running app process).
- Layout: no form-panel scrollbar; button/status reserved in bottom grid rows; error area fixed (~2 lines).
- Inputs: custom templates (white + `#F1D8C7` border + corner radius 10).
- Loading: `IsLoadingBranches` disables form and shows loading caption; login busy uses `IsBusy` separately.
- Config error UI string: `Chưa cấu hình kết nối Firebird. Liên hệ quản trị viên.`
- Auth path unchanged (Infrastructure not modified). Valid login still depends on local `KAZUKO_FIREBIRD_PASSWORD`.

## Result

Ready for visual check at 100% and 125% font scale by operator.
