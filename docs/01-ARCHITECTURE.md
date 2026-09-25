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
