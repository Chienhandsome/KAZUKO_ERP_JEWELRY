# Hệ thống cập nhật module một file

## Định dạng

`.kazuko-module` là ZIP có cấu trúc chuẩn:

```text
module.json
bin/Kazuko.Modules.Warehouse.dll
migrations/0026-add-field.sql
resources/...
signature.sig
```

Manifest tối thiểu:

```json
{
  "id": "Kazuko.Warehouse",
  "name": "Kho",
  "version": "2.1.0",
  "entryAssembly": "bin/Kazuko.Modules.Warehouse.dll",
  "sha256": "HEX_SHA256_OF_ENTRY_ASSEMBLY",
  "minimumHostVersion": 1
}
```

## Pipeline production bắt buộc

1. Kiểm tra extension, ZIP structure và chống path traversal.
2. Parse manifest bằng schema nghiêm ngặt.
3. Kiểm tra host compatibility và dependency.
4. Xác minh SHA-256 cho toàn bộ payload khai báo.
5. Xác minh chữ ký RSA/ECDSA bằng public key được nhúng trong host.
6. Backup database và module hiện hành.
7. Extract vào staging directory riêng.
8. Chạy preflight và migration trong transaction khi Firebird cho phép.
9. Ghi trạng thái `PendingActivation`; activation ở lần khởi động tiếp theo.
10. Health-check; thành công thì commit version, thất bại thì rollback.

Không load assembly mới vào process trước khi các bước kiểm tra hoàn tất. Không cho phép module tự ghi file ngoài thư mục dữ liệu được cấp.

## Trạng thái hiện tại

`ModulePackageInstaller` đã thực hiện kiểm tra extension, manifest, entry assembly, SHA-256 và extract vào staging. Đây là foundation, chưa phải installer production. Các phần còn thiếu trước khi sử dụng cho khách hàng:

- Chống ZIP path traversal bằng kiểm tra canonical path cho mọi entry.
- Chữ ký số và quản lý public key.
- Hash toàn bộ payload.
- Dependency/host compatibility validation.
- Firebird backup, migration ledger, activation và rollback.
- Audit log và giao diện quản trị update.

Agent không được mô tả foundation hiện tại là cơ chế cập nhật production hoàn chỉnh.
