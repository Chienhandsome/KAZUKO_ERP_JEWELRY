# Hệ thống cập nhật module một file

## Định dạng (packageFormatVersion = 2)

`.kazuko-module` là ZIP. Metadata ngoài payload:

```text
module.json
signature.sig
```

Mọi tệp khác phải được khai báo trong `payload` của manifest. Manifest legacy (chỉ hash entry assembly, không có danh sách payload / `packageFormatVersion`) **bị từ chối**.

Manifest tối thiểu (v2):

```json
{
  "packageFormatVersion": 2,
  "moduleId": "Kazuko.Warehouse",
  "displayName": "Kho",
  "moduleVersion": "2.1.0",
  "minimumHostVersion": 1,
  "entryAssembly": "bin/Kazuko.Modules.Warehouse.dll",
  "publisher": "Kazuko",
  "keyId": "kazuko-2026-01",
  "signatureAlgorithm": "RSASSA-PSS-SHA256",
  "payload": [
    {
      "path": "bin/Kazuko.Modules.Warehouse.dll",
      "sha256": "HEX_SHA256",
      "length": 12345
    }
  ]
}
```

### Chữ ký

- Thuật toán allow-list hiện tại: `RSASSA-PSS-SHA256`.
- `signature.sig` là chữ ký RSA-PSS-SHA256 trên **bytes nguyên bản** của `module.json`.
- Vì manifest chứa hash/length toàn bộ payload, chữ ký ràng buộc cả package.
- Public key tra cứu theo `publisher + keyId` từ trusted store của host (`ITrustedPublicKeyStore`).
- Private signing key **không** được lưu trong repository hay build client công khai.

### Giới hạn tài nguyên (mặc định)

| Limit | Giá trị mặc định |
|---|---|
| Max entry count | 256 |
| Max entry size | 50 MiB |
| Max total uncompressed | 200 MiB |
| Max compression ratio | 100:1 |

## Pipeline production bắt buộc

1. Kiểm tra extension, ZIP structure và chống path traversal (canonical path từng entry).
2. Parse manifest nghiêm ngặt (từ chối field lạ / thiếu / legacy).
3. Kiểm tra host compatibility (`minimumHostVersion`).
4. Xác minh SHA-256 + length cho **toàn bộ** payload khai báo; không thừa/thiếu file.
5. Xác minh chữ ký RSA-PSS bằng public key tin cậy.
6. Backup database và module hiện hành. *(TASK-007B+)*
7. Extract thủ công vào staging directory riêng (sau khi validation thành công).
8. Chạy preflight và migration. *(TASK-007B+)*
9. Ghi trạng thái `PendingActivation`; activation ở lần khởi động tiếp theo. *(TASK-007B+)*
10. Health-check; thành công thì commit version, thất bại thì rollback. *(TASK-007B+)*

Không load assembly mới vào process trước khi các bước kiểm tra hoàn tất. Không cho phép module tự ghi file ngoài thư mục dữ liệu được cấp.

## Trạng thái hiện tại (sau TASK-007B1)

`ModulePackageInstaller` (007A):

- Strict manifest v2 + payload allow-list
- ZIP path safety (traversal, absolute, ADS, reserved names, symlink metadata, duplicate canonical path)
- Resource limits chống ZIP bomb
- Hash/length toàn bộ payload (stream)
- Chữ ký RSA-PSS-SHA256 bắt buộc qua `ITrustedPublicKeyStore`
- Host compatibility qua `IHostVersionProvider`
- Staging an toàn (temp → staged, cleanup khi lỗi)

`IModuleActivationCatalog` (007B1):

- Promote thư mục `.staged` → `Modules/Installed/{moduleId}/{version}`
- Ghi `activation-state.json` atomic (+ `.bak`) với trạng thái `PendingActivation`
- Khóa `activation.lock` exclusive cross-process
- Kiểm tra lại hash payload khi promote; từ chối cùng version khác nội dung

**Đây vẫn chưa phải installer production hoàn chỉnh.** Còn thiếu (TASK-007B2+):

- Dynamic module discovery / `AssemblyLoadContext`
- Firebird backup (`gbak`), migration ledger runner, cold-start activation và rollback
- Provisioning public key production và quy trình ký phát hành
- Audit log và giao diện Import/Update

Agent không được mô tả TASK-007A/B1 là cơ chế cập nhật production hoàn chỉnh.
