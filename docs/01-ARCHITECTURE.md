# Kiến trúc

## Dependency direction

```text
Kazuko.App ───────────────┐
Modules ───────┐          │
Shared.UI ─────┼──> Kazuko.Core
Infrastructure ┘          │
Kazuko.App ─────────> Infrastructure
```

`Kazuko.Core` không tham chiếu WPF, Firebird hoặc project nghiệp vụ. `Infrastructure` hiện thực các interface về lưu cấu hình, database, file, update và dịch vụ hệ thống. `Shared.UI` chứa theme, style và control WPF dùng chung. `Kazuko.App` chỉ composition/bootstrap và host shell.

## Luồng chuẩn

```text
View → ViewModel → Application Service → Repository → Firebird
```

- View binding state và command.
- ViewModel điều phối use case, không chứa SQL hay biết chi tiết Firebird.
- Application Service giữ transaction và quy tắc nghiệp vụ liên quan nhiều repository.
- Repository chịu trách nhiệm persistence.

## Module

Module triển khai `IKazukoModule`:

```csharp
public interface IKazukoModule
{
    string Id { get; }
    Version Version { get; }
    void RegisterServices(IServiceRegistry services);
    void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services);
}
```

Quy ước ID: `Kazuko.<Domain>`, ví dụ `Kazuko.Warehouse`. Navigation key dùng chữ hoa ổn định như `WAREHOUSE` hoặc `WAREHOUSE.MATERIALS`. Permission dùng dạng `<domain>.<resource>.<action>`, ví dụ `warehouse.material.read`.

`NavigationItem` là metadata đóng góp bởi module cho cả shell và trang chủ. Ngoài key/label/View factory, descriptor có icon URI do assembly module sở hữu, mô tả, cờ hiện trên sidebar/trang chủ và trạng thái placeholder. `MainWindow` và `DashboardView` chỉ render registry, không switch theo key nghiệp vụ.

Các route placeholder dùng để dựng khung giao diện trước khi module nghiệp vụ hoàn chỉnh. Khi module thật đăng ký cùng key, `ModuleNavigationRegistry` chỉ cho phép route thật thay thế placeholder; trùng hai route thật vẫn bị từ chối. Home tile yêu cầu điều hướng qua `INavigationService`, vì vậy module không tham chiếu ngược tới `Kazuko.App`.

Đây là contract để module được kích hoạt ở lần khởi động sau có thể tự đóng góp menu/home tile. Việc khám phá và load assembly từ gói `.kazuko-module` vẫn thuộc pipeline activation, chưa được coi là hoàn thiện chỉ vì contract UI đã tồn tại.

## Session và giao tiếp giữa module

- `AuthenticatedSession` + `ICurrentSessionAccessor` (Core) giữ UserId/Username/DisplayName/RoleCode/Branch* sau login; App đăng ký singleton và clear khi logout. Không chứa mật khẩu.
- Module nghiệp vụ không tham chiếu lẫn nhau. Dialog/use case dùng chung (ví dụ khách hàng) expose contract WPF-neutral trong Core (`ICustomerEditorService`, `CustomerEditResult`) và implementation nằm trong module sở hữu.
- SQL chỉ ở Infrastructure (`FirebirdCustomerRepository`). Module Customers tham chiếu Core + Shared.UI; repository được App đăng ký tại composition root.

## Module package updates (TASK-007A)

Core contracts trong `Kazuko.Core.Updates`:

- `IModulePackageInstaller` — validate + stage only (không load DLL).
- `ITrustedPublicKeyStore` / `IHostVersionProvider` / `IModulePackageSignatureVerifier`.
- Manifest v2: `packageFormatVersion`, payload allow-list (path/sha256/length), publisher/keyId, `RSASSA-PSS-SHA256`.

Infrastructure `ModulePackageInstaller` thực hiện kiểm tra ZIP an toàn và staging.
`IModuleActivationCatalog` (TASK-007B1) promote staging → Installed và ghi `PendingActivation`.
Cold-start activation, migration và UI Import thuộc task sau.

## Form động

Database chỉ lưu mô tả đã giới hạn:

- Control type nằm trong allow-list.
- Binding trỏ tới field/use case đã đăng ký.
- Validation dùng rule type và tham số JSON.
- Action trỏ tới command key do module cung cấp.
- Không lưu/running arbitrary code.

Schema dự kiến gồm `FORM_DEFINITIONS`, `FORM_FIELDS`, `FORM_ACTIONS`, `FORM_PERMISSIONS` và version/audit tương ứng. JSON phải được validate bằng schema trước khi kích hoạt.

## Database migration

- Migration chỉ tiến về phía trước và có số version duy nhất.
- Nội dung migration có checksum; production phải dừng nếu checksum của migration đã chạy bị thay đổi.
- Migration module chạy sau backup và trước activation.
- Mỗi migration phải idempotent khi hợp lý và có kế hoạch phục hồi dữ liệu.

## Báo cáo / in ấn

Quyết định mẫu báo cáo A4 và xem trước/in: xem `docs/09-REPORTING.md` (FastReport Open Source + file `.frx`).
