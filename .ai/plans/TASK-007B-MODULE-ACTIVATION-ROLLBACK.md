# TASK-007B – MODULE ACTIVATION, MIGRATION, BACKUP AND ROLLBACK

> **Loại tài liệu:** Đặc tả implementation-ready (phân tích / thiết kế)  
> **Ngày:** 2026-09-25  
> **Phạm vi:** Chỉ thiết kế. Không sửa source, solution, tracked docs, database. Không bắt đầu TASK-007C / Supplier. Không đổi TASK-007A.  
> **Căn cứ:** `AGENTS.md`, `.ai/PROJECT.md`, `docs/00–04`, `docs/05-EMBEDDED-FIREBIRD-OPERATIONS.md`, `Kazuko.Core.Updates`, `Kazuko.Infrastructure.Updates`, `App.xaml.cs`, `ModuleContracts.cs`, `ServiceRegistry`, `ModuleNavigationRegistry`, commits TASK-007A (`55a1d4f`, `2481c4d`, `79dfe1f`), và đối chiếu tương thích với `.ai/plans/TASK-008-SUPPLIER-VERTICAL-SLICE.md` (không sửa file 008).

---

## 0. Hiện trạng sau TASK-007A (baseline không được phá)

### Đã có

| Thành phần | Hành vi |
|---|---|
| `IModulePackageInstaller.ValidateAndStageAsync` | Validate ZIP + manifest v2 + hash + RSA-PSS-SHA256 → extract vào staging. **Không** load DLL. |
| Staging root hiện tại | `%LocalAppData%\KazukoERP\Updates` (composition trong `App.xaml.cs`) |
| Staged folder | `{safeId}-{safeVersion}-{guid}.staged` chứa payload + `module.json` + `signature.sig` |
| Fail-closed trust | `EmptyTrustedPublicKeyStore` → từ chối mọi gói chưa provision key |
| Module host hiện tại | `App` đăng ký cứng `DashboardModule` rồi `CustomersModule`; không có discovery động |

### Còn thiếu (đối tượng TASK-007B)

- Catalog versioned + `activation-state.json`
- Promote staged → Installed; pointer Active/Previous/Quarantine
- Backup Firebird (`gbak` 5.0.4) + migration ledger
- Startup activation + health-check + rollback
- `AssemblyLoadContext` policy
- Trusted public key provisioning production
- Audit events activation lifecycle
- Import UI → **TASK-007C** (ngoài scope B)

### Ràng buộc kiến trúc bắt buộc

- Core không phụ thuộc WPF/Infrastructure.
- SQL chỉ trong Infrastructure; View/ViewModel không chạy SQL.
- Không ghi đè DLL đang chạy trong process; không hot-reload View WPF.
- Không nhận SQL ngoài signed payload.
- Pipeline production: validate → backup → stage → migrate → activate → health-check; fail → rollback.

---

## 1. Recommended architecture

```text
Import (.kazuko-module)
        │
        ▼
┌───────────────────────────┐
│ ModulePackageInstaller    │  TASK-007A (giữ nguyên contract)
│ Validate + Stage only     │
└─────────────┬─────────────┘
              │ Staging/*.staged
              ▼
┌───────────────────────────┐
│ ModuleActivationCatalog   │  007B1
│ Promote → Installed/{id}/{ver}
│ State: PendingActivation  │
└─────────────┬─────────────┘
              │ restart required
              ▼
┌───────────────────────────┐
│ StartupActivationPipeline │  007B4 (orchestrator)
│ 1. single-instance lock   │
│ 2. re-verify hash/sig     │
│ 3. backup (gbak)          │  007B3
│ 4. migrations             │  007B3
│ 5. swap Active pointer    │
│ 6. load ALC + register    │  007B2
│ 7. health-check           │
│ 8. commit / rollback      │
└───────────────────────────┘
              │
              ▼
     Login / Main shell
```

**Nguyên tắc then chốt**

1. **Import ≠ Activate.** Import chỉ tạo `PendingActivation`. Activation chỉ ở cold start.
2. **Một version active / moduleId.** Nhiều version được giữ dưới `Installed/{moduleId}/{version}`.
3. **Built-in là baseline.** Module ship kèm app luôn sẵn sàng làm fallback; installed override chỉ khi trust + version + health-check đạt.
4. **Migration trước pointer Active.** Schema phải ổn trước khi process coi module là Active.
5. **Non-collectible ALC** cho module đã activate trong process hiện tại (giải thích mục 4). Unload chỉ bằng restart.
6. **Audit best-effort** sau commit trạng thái; lỗi audit không đảo ngược activation đã commit (cùng pattern `AuditLogWriter`).

### Root dữ liệu đề xuất (production)

Theo `docs/05` (tách binary vs data). Activation artifacts **không** đặt trong `Program Files`:

```text
%ProgramData%\Kazuko ERP\
  data\
    data.fdb
    backups\                          ← gbak .fbk (+ verify leftovers)
  modules\
    activation-state.json
    activation.lock
    Staging\                          ← có thể symlink/junction tới LocalAppData Updates, hoặc migrate path
    Installed\{moduleId}\{version}\
    Active\{moduleId}                 ← junction/directory pointer → Installed\...\version
    Previous\{moduleId}               ← pointer version trước
    Quarantine\{moduleId}\{version}\
  keys\
    trusted-public-keys.json          ← public only
  logs\
```

**Dev tương thích 007A:** giữ `%LocalAppData%\KazukoERP\` với cùng layout con (`Updates` = Staging legacy alias; `Modules\...`). Composition root resolve một `IModuleLayoutPaths`. Không đổi package format v2.

---

## 2. Directory layout (chi tiết)

```text
Modules/
├── activation-state.json          # source of truth cho state machine
├── activation.lock                # exclusive file lock (single activator)
├── Staging/
│   └── {safeId}-{safeVersion}-{guid}.staged/     # output 007A
│       ├── module.json
│       ├── signature.sig
│       ├── bin/...
│       └── migrations/...          # optional signed payload
├── Installed/
│   └── {moduleId}/
│       └── {moduleVersion}/        # immutable sau promote (re-hash trước dùng)
│           ├── module.json
│           ├── signature.sig
│           ├── bin/
│           └── migrations/
├── Active/
│   └── {moduleId}/                 # chỉ chứa marker + version.txt hoặc junction
│       └── CURRENT → ..\..\Installed\{moduleId}\{ver}
├── Previous/
│   └── {moduleId}/
│       └── CURRENT → previous version (hoặc BUILTIN sentinel)
└── Quarantine/
    └── {moduleId}/{moduleVersion}/ # copy hoặc move từ Installed khi fail nặng
```

### Quy tắc layout

| Quy tắc | Chi tiết |
|---|---|
| Nhiều version | Chỉ dưới `Installed/{id}/`; không xóa ngay sau activate |
| Một active | `Active/{id}` trỏ đúng một version; catalog enforce mutex |
| Không silently canonicalize | Promote dùng cùng path rules 007A; từ chối empty segment / `//` |
| Immutable install | Sau promote thành công, không ghi đè cùng `{id}/{version}`; package trùng version + khác hash → fail / quarantine |
| Staging cleanup | Sau promote OK có thể xóa `.staged`; thất bại giữ để audit (TTL) |

### `activation-state.json` (schema đề xuất)

```json
{
  "schemaVersion": 1,
  "hostVersion": 1,
  "updatedAtUtc": "2026-09-25T08:00:00Z",
  "modules": {
    "Kazuko.Warehouse": {
      "state": "PendingActivation",
      "activeVersion": "1.0.0",
      "previousVersion": null,
      "pendingVersion": "2.1.0",
      "source": "Installed",
      "publisher": "Kazuko",
      "keyId": "kazuko-2026-01",
      "packageSha256": "...",
      "lastError": null,
      "backupId": null,
      "attemptId": "guid",
      "builtInFallbackAllowed": true
    }
  },
  "activationQueue": ["Kazuko.Warehouse"]
}
```

Ghi: temp file cùng volume → `File.Replace` / move atomic. Corrupt → recovery mục 8.

---

## 3. Activation state machine

### States

| State | Ý nghĩa |
|---|---|
| `Staged` | Có thư mục `.staged`; chưa promote / chưa queue activation |
| `PendingActivation` | Đã promote vào Installed; chờ cold start |
| `Activating` | Startup đang chạy backup/migrate/load/health |
| `Active` | Health-check OK; pointer Active = version này |
| `Failed` | Activation lỗi; chưa hoàn tất rollback hoặc chờ quyết định |
| `RollbackPending` | Cần rollback DLL/DB ở lần start tiếp (crash giữa chừng) |
| `RolledBack` | Đã về previous/built-in; pending version không còn active |
| `Quarantined` | Version bị cô lập; không được chọn activate lại cho đến khi admin/clear |

`Staged` có thể là trạng thái catalog phụ (sau 007A installer chưa ghi catalog). 007B1: khi “Accept staged for activation” → promote + `PendingActivation`.

### Transition table

| From | Event | To | Side effects |
|---|---|---|---|
| *(none)* | ValidateAndStage OK | `Staged` | `.staged` tồn tại (007A) |
| `Staged` | AcceptForActivation | `PendingActivation` | Copy/move → Installed; ghi previous; audit `MODULE_ACTIVATION_PENDING` |
| `PendingActivation` | Startup begins (lock OK) | `Activating` | `attemptId`; audit |
| `Activating` | Backup OK + Migrate OK + Load OK + Health OK | `Active` | Swap Active; clear pending; Previous = old; audit `MODULE_ACTIVATED` |
| `Activating` | Backup fail | `Failed` → auto `RolledBack`/`Pending` giữ | Không đổi Active; không migrate; quarantine optional |
| `Activating` | Migration fail | `RollbackPending`/`Failed` | Restore DB từ backup nếu migration đã đụng schema; DLL pointer không đổi |
| `Activating` | DLL/register/health fail | `RollbackPending` | Active giữ previous/built-in; pending → Quarantine nếu hash/sig/load suspicious |
| `RollbackPending` | Startup recovery | `RolledBack` hoặc `Active`(old) | Hoàn tất rollback; audit `MODULE_ROLLED_BACK` |
| `Failed` | Admin retry / new package | `Staged`/`PendingActivation` | Không auto-skip signature |
| `Active` | New package accept | `PendingActivation` (pendingVersion set) | activeVersion giữ nguyên đến restart success |
| `*` | Critical integrity fail | `Quarantined` | Move version; audit `MODULE_QUARANTINED` |

### Crash recovery

| Thời điểm crash | Recovery khi mở lại |
|---|---|
| Trong `PendingActivation` | Giữ queue; chạy activation lại |
| Trong `Activating` trước swap Active | Coi như chưa activate; Active cũ còn; thử lại hoặc fail-safe về Active cũ |
| Trong `Activating` sau migrate, trước health OK | `RollbackPending`: restore DB từ `backupId` nếu ledger ghi migration của attempt này; Active pointer không được là version mới |
| Trong `Activating` sau swap Active, trước health commit | `RollbackPending`: trỏ lại Previous; quarantine version mới; DB restore nếu migration thuộc attempt |
| State file nửa vời | Xem mục 8; ưu tiên Previous/Active filesystem markers |

**App tắt đột ngột:** không bao giờ để process khác “sửa DLL đang map”. Chỉ đổi directory pointer khi không có process giữ lock; process mới đọc state và hoàn tất.

---

## 4. Activation timing

```text
OnStartup:
  1. Resolve paths + acquire activation.lock (fail → show blocking error, exit)
  2. Load/repair activation-state.json
  3. If queue has PendingActivation / RollbackPending / Activating(orphan):
       run StartupActivationPipeline (no UI shell yet)
  4. Discover modules: built-in + Active installed
  5. Register services/navigation
  6. Health-check gate (host + each activated module hook optional)
  7. Release lock (or keep read lock until exit — prefer release after pipeline)
  8. Apply appearance → ShowLogin → shell
```

**Cấm**

- Thay file trong thư mục Active trong lúc process đã load assembly đó.
- `Assembly.LoadFrom` package ngay sau Import.
- Hot reload / recreate View từ DLL mới trong cùng session.
- Mở login trước khi activation/rollback pending được xử lý xong (trừ chế độ safe-mode có chủ đích — không mặc định).

Import UI (007C) chỉ gọi installer + `AcceptForActivation` → message “Khởi động lại để áp dụng”.

---

## 5. Dynamic module discovery & AssemblyLoadContext

### Pipeline discovery (mỗi Active module)

1. Đọc `module.json` từ `Installed/{id}/{activeVersion}` (không tin path Active nếu mismatch catalog).
2. Re-verify: mọi payload SHA-256/length + signature RSA-PSS với `ITrustedPublicKeyStore` **hiện tại** (key có thể đã revoke → fail → quarantine).
3. Kiểm tra `minimumHostVersion` lại.
4. Tạo `ModuleLoadContext` (custom `AssemblyLoadContext`).
5. `LoadFromAssemblyPath(entryAssembly)` trong version directory.
6. Reflection: đúng **một** public non-abstract type implement `IKazukoModule` trong entry assembly (không scan dependency bừa).
7. `RegisterServices` / `RegisterNavigation`.
8. Xử lý conflict (dưới đây).

### AssemblyLoadContext policy (quyết định)

| Câu hỏi | Quyết định | Lý do |
|---|---|---|
| Collectible? | **Non-collectible** cho module Active | Activation chỉ qua restart → không cần unload in-process. WPF pack URI + static holders + DI singletons giữ type forever; collectible ALC dễ leak/complex mà không lợi. |
| Shared assemblies (`Kazuko.Core`, `Kazuko.Shared.UI`, WPF, BCL) | `Load` → return `null` để default context (host) resolve | Tránh **type identity split** (`IKazukoModule` từ 2 bản Core). |
| Module-private deps | Load từ `Installed/{id}/{ver}` only (prefer that dir) | Không probe `PATH`, không `AppContext.BaseDirectory` tùy ý cho unknown DLL. |
| Allow-list resolve | Chỉ: (a) host shared set, (b) files có trong signed payload của version đó | Dependency hijacking mitigation. |
| DLL ngoài allow-list | Throw → activation fail → rollback | Không shadow-copy từ Downloads. |
| WPF resource pack URI | Entry assembly phải load như normal (non-collectible) + assembly name khớp pack URI `/{AssemblyName};component/...` | Built-in modules đã dùng pattern này; installed phải giữ cùng assembly name khi override. |
| Activation timing | Chỉ sau restart, trong pipeline trước login | Không load giữa session. |

```csharp
// Pseudocode — contract shape only (không implement trong task này)
sealed class ModuleLoadContext : AssemblyLoadContext
{
    // isCollectible: false
    protected override Assembly? Load(AssemblyName name)
    {
        if (HostSharedAssemblyNames.Contains(name.Name)) return null; // host
        if (TryLoadFromModuleDirectory(name, out var asm)) return asm;
        throw new FileNotFoundException($"Module dependency not allowed: {name}");
    }
}
```

Host shared set tối thiểu: `Kazuko.Core`, `Kazuko.Shared.UI`, `PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Xaml`, net* shared framework. Infrastructure **không** đưa vào module allow-list mặc định — module nói chuyện qua Core contracts; repository vẫn do App composition đăng ký (như Customers hiện tại) cho đến khi có model plugin service an toàn hơn.

### Conflicts

| Conflict | Hành vi |
|---|---|
| Hai `IKazukoModule` cùng `Id` (built-in + installed) | Chọn **một** theo policy mục 6; không gọi `Register*` hai lần |
| Trùng navigation key hai route thật | `ModuleNavigationRegistry` đã throw — activation fail → rollback |
| Placeholder + real | Giữ behavior hiện tại: real thay placeholder |
| Missing dependency / host version | Fail activation; không partial register |
| 0 hoặc >1 `IKazukoModule` trong entry | Fail |

---

## 6. Built-in module strategy

### Baseline

| Module | Id | Vai trò |
|---|---|---|
| Dashboard | `Kazuko.Dashboard` | Built-in bắt buộc (shell home + placeholders) |
| Customers | `Kazuko.Customers` | Built-in thật; có thể bị override bởi installed **chỉ khi** policy cho phép |
| Authentication | *(không qua IKazukoModule hiện tại)* | Host-owned; không package-override trong 007B |

### Override rules

1. Built-in luôn compile vào app; đăng ký **trừ khi** catalog có Active installed cùng `moduleId` với `state=Active` và `trust` còn hiệu lực.
2. Installed override built-in **chỉ khi**:
   - Signature + key còn trusted (không revoked),
   - `moduleVersion` ≥ built-in version (SemVer) — **cấm downgrade** trừ admin explicit + signed “allowDowngrade” flag trong manifest tương lai (mặc định **không** có trong v2 → reject downgrade),
   - Health-check OK lần activate trước (hoặc đang Activating lần đầu).
3. Khi installed fail: rollback về `Previous` nếu là installed khác; nếu Previous null → **built-in**.
4. Navigation: không đăng ký hai module thật cùng key — chọn đúng một implementation trước khi `RegisterNavigation`.
5. Dashboard placeholders: installed business modules (Warehouse, Supplier, …) thay key placeholder; không thay `Kazuko.Dashboard` trừ khi package Id đúng là Dashboard (hiếm; cần review riêng).

### Recommendation mặc định 007B

- **Dashboard: không cho package override** (`builtInFallbackAllowed=true`, `allowOverride=false` trong host policy).
- **Customers: cho phép override** bởi signed newer package (phục vụ hotfix field), rollback về built-in khi fail.
- Module mới (Warehouse…): chỉ có installed path (không built-in).

---

## 7. Migration pipeline

### Tương thích TASK-008 / schema hiện có

Từ plan TASK-008: DB đã có bảng **`SCHEMA_MIGRATIONS`** và **`BACKUP_HISTORY`** (legacy WinForms migrator 46 bản). 007B **không** invent ledger mới nếu có thể map:

Đề xuất đọc schema thật trong task 007B3 (không làm trong tài liệu này / không đụng DB lúc thiết kế):

- Reuse `SCHEMA_MIGRATIONS` với cột version + checksum (đối chiếu WinForms khi implement).
- Namespace migration:  
  - Host/core: `host:{version}` hoặc số nguyên toàn cục như legacy  
  - Module: `module:{moduleId}:{migrationId}` để tránh đụng version host/Supplier

**Không sửa file TASK-008.** Khi 008 cấp migration Supplier, chúng phải:

- Đi qua cùng ledger,
- Chỉ chạy từ signed source (host release hoặc module package),
- Không SQL trong View/ViewModel.

### Payload migration trong `.kazuko-module`

Allow-list path prefix đề xuất (mở rộng kiểm tra 007A **không đổi format v2 fields**; chỉ convention payload paths):

```text
migrations/001_init.sql
migrations/001_init.sha256   # optional nếu đã có trong manifest payload hash
migrations/manifest.json     # optional ordered list { id, file, checksum }
```

Mỗi file migration **phải** nằm trong `payload[]` đã ký. Không đọc `.sql` ngoài list.

### Contract đề xuất (Core)

```csharp
public sealed record ModuleMigrationScript(
    string MigrationId,      // unique within module
    string RelativePath,
    string Sha256,
    long Length);

public interface IModuleMigrationPlanner
{
    IReadOnlyList<ModuleMigrationScript> ReadFromInstalledModule(string moduleRoot);
}

public interface ISchemaMigrationLedger
{
    Task<bool> IsAppliedAsync(string scope, string migrationId, CancellationToken ct);
    Task<string?> GetAppliedChecksumAsync(string scope, string migrationId, CancellationToken ct);
    Task RecordAppliedAsync(string scope, string migrationId, string checksum, CancellationToken ct);
}

public interface IModuleMigrationRunner
{
    /// <summary>Runs pending scripts inside transaction when Firebird supports; never accepts ad-hoc SQL.</summary>
    Task ApplyPendingAsync(
        string moduleId,
        IReadOnlyList<ModuleMigrationScript> scripts,
        Func<string, Task<string>> readSqlUtf8,
        CancellationToken ct);
}
```

### Thứ tự bắt buộc

1. Re-verify signature/hash installed tree  
2. `gbak` backup → verify (mục 8) → ghi `BACKUP_HISTORY` / backupId vào state  
3. Load pending migrations; với mỗi script:  
   - Nếu ledger có cùng version/id **và cùng checksum** → skip  
   - Nếu cùng id **khác checksum** → **FAIL** (tamper / divergent)  
   - Else execute SQL từ bytes đã hash; transaction nếu engine cho phép DDL batch  
4. Chỉ khi migration set complete → tiếp tục load DLL / swap Active  
5. Module code **không** được tự `FbCommand` migration ad-hoc từ ViewModel

### Firebird notes

- Embedded: đóng pool / hạn chế connection trong lúc gbak khi cần.
- DDL trong Firebird thường auto-commit từng statement — runner phải tracking từng statement + backup restore là nguồn rollback DB chính (không dựa vào transaction DDL).
- **Cấm** `DROP` tự động để “rollback schema” nếu migration đã tạo dữ liệu nghiệp vụ.

---

## 8. Backup and rollback

### gbak 5.0.4

- Binary: `FirebirdRuntime.HomeDirectory\gbak.exe` (cùng bundle `docs/05`).
- Backup path:  
  `%ProgramData%\Kazuko ERP\data\backups\module-activate_{moduleId}_{version}_{yyyyMMddHHmmss}_{attemptId}.fbk`
- Command shape (implement 007B3): `gbak -b -user ... -password ... database.fdb backup.fbk` (password từ profile DPAPI/env — không hard-code).
- **Verify:** restore thử vào temp DB path `...\backups\verify\{attemptId}.fdb` bằng `gbak -c` / `-r` rồi xóa temp; fail verify = backup fail.
- Ghi `BACKUP_HISTORY` nếu schema cho phép (reuse); không tạo bảng mới nếu đã có.

### Retention

| Loại | Giữ |
|---|---|
| Successful activation backup | N=5 last per moduleId (config) |
| Failed attempt backup | ≥1 until quarantine cleared |
| Verify temp DB | xóa ngay sau verify |

### Khi nào restore DB vs chỉ rollback DLL

| Tình huống | DB restore | DLL / pointer |
|---|---|---|
| Backup fail | Không | Không đổi Active; abort |
| Migration fail (sau khi chạy SQL) | **Có** (từ backupId) | Active không đổi |
| Migration fail (chưa chạy SQL) | Không bắt buộc | Active không đổi |
| DLL load / register / health fail **sau** migration OK | **Không auto DROP**; tùy policy: (A) giữ schema mới + RolledBack DLL về previous **chỉ khi** previous tương thích schema; (B) nếu previous không tương thích → restore DB + DLL previous | Quarantine version mới |
| DLL fail **trước** migration | Không | Active cũ |
| App crash giữa activation | Theo `RollbackPending` + backupId | Mục 3 |

**Khuyến nghị mặc định:** Migration và module version đi cùng nhau (package không ship DLL cần schema mới mà previous DLL không đọc được). Nếu health fail sau migrate → **restore DB + rollback DLL** (an toàn hơn để schema/DLL lệch).

### Failure / rollback matrix

| Failure | State result | DB | Module files | User-visible |
|---|---|---|---|---|
| Backup fail | Failed → remain previous Active | nguyên | pending giữ / không Active | Chặn activate; message an toàn |
| Migration fail | RollbackPending→RolledBack | restore backup | Active cũ | Không mở shell với module mới |
| DLL load fail | RolledBack / Quarantined | restore nếu đã migrate | Previous/built-in | Log + audit |
| Registration fail (nav dup) | như trên | như trên | như trên | |
| Health-check fail | như trên | restore nếu migrated | Quarantine new | |
| Crash mid-Activating | RollbackPending | reconcile bằng backupId + ledger attempt | Previous | Blocking recovery UI tối thiểu (optional 007C) |
| Audit write fail | **không đổi** state đã commit | n/a | n/a | Best-effort retry / file log |

---

## 9. Crash-safe persistence & concurrency

1. **Atomic state write:** serialize JSON → `activation-state.json.tmp` → flush → `File.Replace`/`Move` overwrite.
2. **File lock:** `activation.lock` mở `FileShare.None`; chỉ một process activation.
3. **Second instance:** nếu lock bận → thoát với mã/message “Ứng dụng đang cập nhật module”.
4. **Previous trước Active:** luôn ghi `previousVersion` + Previous pointer **trước** khi đổi Active pointer.
5. **Corrupt JSON:**  
   - Thử `activation-state.json.bak`  
   - Else rebuild tối thiểu từ filesystem: Active/Previous markers + Installed manifests; state = `RollbackPending` nếu Activating orphan  
   - Fail closed: không activate pending chưa verify
6. **Ledger vs state:** DB ledger là nguồn thật cho migration applied; file state là nguồn thật cho DLL pointers.

---

## 10. Trust / public key provisioning

| Yêu cầu | Thiết kế |
|---|---|
| Không private key trong app/repo | Tiếp tục; signing offline CI/HSM |
| Public key bundled | `%ProgramData%\Kazuko ERP\keys\trusted-public-keys.json` **hoặc** embedded resource read-only trong Infrastructure + optional overlay file |
| Lookup | `publisher` + `keyId` → SPKI DER (như `ITrustedPublicKeyStore`) |
| Rotation | Nhiều key đồng thời; package dùng keyId mới; cũ vẫn verify gói cũ |
| Revocation | Entry `revoked:true` / list riêng; FindPublicKey trả null → fail + audit unknown/revoked |
| UI bypass chữ ký | **Cấm** mọi API/flag production |
| Dev | `InMemoryTrustedPublicKeyStore` chỉ trong test; Dev overlay qua env `KAZUKO_TRUST_KEYS_PATH` nếu cần — không ship default empty bypass |

Composition: thay `EmptyTrustedPublicKeyStore` bằng `FileTrustedPublicKeyStore` khi file tồn tại; vẫn fail-closed nếu file trống.

---

## 11. Audit

### Events

| Event | Khi nào | ENTITY |
|---|---|---|
| `MODULE_PACKAGE_IMPORTED` | Stage OK (007A hoặc wrapper) | moduleId/version/stagingPath |
| `MODULE_ACTIVATION_PENDING` | AcceptForActivation | |
| `MODULE_BACKUP_CREATED` | gbak + verify OK | backupId/path |
| `MODULE_MIGRATION_APPLIED` | mỗi script hoặc batch | migrationId/checksum |
| `MODULE_ACTIVATED` | health OK commit | |
| `MODULE_ACTIVATION_FAILED` | bất kỳ fail | lastError code |
| `MODULE_ROLLED_BACK` | rollback xong | toVersion |
| `MODULE_QUARANTINED` | quarantine | reason |

### Reconcile khi audit fail

- Giống `AuditLogWriter`: catch, không throw.
- Optional append-only `%ProgramData%\Kazuko ERP\logs\module-audit-fallback.jsonl`.
- Startup có thể best-effort “catch-up” audit từ state transitions chưa ack (idempotent by attemptId).

---

## 12. Security boundaries (threat model tối thiểu)

| Threat | Host ngăn được? | Ghi chú |
|---|---|---|
| DLL dependency hijacking | **Có (phần lớn)** | ALC allow-list + signed payload only |
| Type identity split | **Có** | Shared asm → default context |
| Tampered staged/installed files | **Có** | Re-hash + re-sig trước activate |
| Rollback/downgrade attack | **Có** | Cấm downgrade mặc định; revoke key |
| Replay package | **Một phần** | Cùng version+hash = idempotent; khác hash cùng version = fail. Timestamp anti-replay không bắt buộc 007B nếu version monotonic |
| Path traversal sau staging | **Có** | Reuse ModulePackagePathRules trên mọi path |
| Unsigned migration | **Có** | SQL chỉ từ signed payload |
| Malicious module filesystem writes | **Một phần** | Policy + code review; **không** có sandbox OS đầy đủ trong 007B |
| Concurrent activation | **Có** | file lock |
| Backup leakage | **Một phần** | ACL ProgramData; backups chứa toàn DB → bảo vệ quyền NTFS; không upload |
| Malicious code in trusted signed module | **Không đủ** | Cần process isolation / AppContainer **tương lai**; 007B trust publisher |

**Ranh giới tương lai:** Out-of-process module host, restricted tokens, capability APIs — ngoài 007B.

---

## 13. Public contracts đề xuất (Core)

Nằm trong `Kazuko.Core.Updates` (và có thể `Kazuko.Core.Modules` cho health). **Không đổi** `IModulePackageInstaller` / manifest v2 trừ khi thêm optional fields đã negotiated (mặc định **không đổi** format).

```csharp
public enum ModuleActivationState
{
    Staged,
    PendingActivation,
    Activating,
    Active,
    Failed,
    RollbackPending,
    RolledBack,
    Quarantined
}

public interface IModuleLayoutPaths
{
    string StagingRoot { get; }
    string InstalledRoot { get; }
    string ActiveRoot { get; }
    string PreviousRoot { get; }
    string QuarantineRoot { get; }
    string ActivationStatePath { get; }
    string ActivationLockPath { get; }
    string DatabaseBackupRoot { get; }
}

public interface IModuleActivationCatalog
{
    Task<ModuleActivationRecord?> GetAsync(string moduleId, CancellationToken ct = default);
    Task AcceptStagedForActivationAsync(string stagingDirectory, CancellationToken ct = default);
    Task SaveAsync(ModuleActivationRecord record, CancellationToken ct = default);
}

public interface IModuleActivationPipeline
{
    /// <summary>Runs at cold start before login UI.</summary>
    Task<ActivationPipelineResult> RunPendingAsync(CancellationToken ct = default);
}

public interface IModuleAssemblyLoader
{
    LoadedKazukoModule LoadVerified(string moduleId, string versionDirectory);
}

public sealed record LoadedKazukoModule(
    IKazukoModule Module,
    Assembly Assembly,
    ModulePackageManifest Manifest);

public interface IModuleHealthCheck
{
    Task<HealthCheckResult> CheckAsync(CancellationToken ct = default);
}

public interface IFirebirdBackupService
{
    Task<BackupResult> BackupAsync(string backupId, CancellationToken ct = default);
    Task VerifyBackupAsync(string backupPath, CancellationToken ct = default);
    Task RestoreAsync(string backupPath, CancellationToken ct = default);
}

// ISchemaMigrationLedger / IModuleMigrationRunner — mục 7
// ITrustedPublicKeyStore — giữ; thêm FileTrustedPublicKeyStore ở Infrastructure
```

`ServiceRegistry` hiện chỉ `AddSingleton` instance — 007B2 có thể cần mở rộng **hạn chế** `TryAdd` / replace policy cho override; nếu đụng contract, làm trong B2 với docs update cùng commit (không làm trong tài liệu code).

---

## 14. Task breakdown

### 007B1 – Activation catalog / state store

| | |
|---|---|
| **Goal** | Layout paths, atomic `activation-state.json`, promote Staging→Installed, Accept→PendingActivation, lock file helpers |
| **Allowed files** | `Kazuko.Core/Updates/*` (new contracts), `Kazuko.Infrastructure/Updates/Activation/*` (new), tests under `Kazuko.Infrastructure.Tests/Updates/Activation/`, optional `App.xaml.cs` composition **chỉ** register catalog (chưa pipeline đầy đủ) |
| **Do not modify** | `ModulePackageInstaller` behavior/signature (được gọi), package format, database, modules UI, TASK-008 files, migration runner |
| **Contracts** | `IModuleLayoutPaths`, `IModuleActivationCatalog`, state enum/records |
| **Acceptance** | Promote idempotent; atomic replace; corrupt JSON recovery unit tests; empty segment paths rejected; one active version invariant |
| **Tests** | State transitions Staged→Pending; crash mid-write recovery; concurrent lock (second writer fails) |
| **Failure cases** | Disk full, lock held, staging missing manifest, version directory already exists with different hash |
| **Depends on** | TASK-007A done |

### 007B2 – Module loader + dependency policy

| | |
|---|---|
| **Goal** | `ModuleLoadContext`, re-verify, single `IKazukoModule`, built-in vs installed selection, navigation conflict → fail |
| **Allowed files** | Core loader contracts; `Infrastructure/Updates/Loading/*`; tests; minimal `App` hook behind flag/pipeline stub |
| **Do not modify** | Installer crypto, Firebird schema, Import UI, Dashboard XAML (trừ buộc), 007A tests trừ helper dùng chung |
| **Contracts** | `IModuleAssemblyLoader`, shared-assembly allow-list |
| **Acceptance** | Host Core types identity OK; hijack DLL ngoài payload bị chối; pack URI smoke test optional |
| **Tests** | Fake module assemblies in testhost (non-WPF Prefer); reject dual IKazukoModule; reject missing dep |
| **Failure cases** | Revoked key, minimumHostVersion, nav duplicate |
| **Depends on** | 007B1 |

### 007B3 – Firebird backup / migration coordinator

| | |
|---|---|
| **Goal** | `gbak` backup+verify+restore; ledger against existing `SCHEMA_MIGRATIONS`; run signed SQL only; integrate `BACKUP_HISTORY` if compatible |
| **Allowed files** | `Infrastructure/Data/*` backup/migration types (new), Core migration contracts, tests (fake gbak / temp FB if available) |
| **Do not modify** | TASK-008 plan/code; Customer repository SQL; View/ViewModels; package format |
| **Contracts** | `IFirebirdBackupService`, `ISchemaMigrationLedger`, `IModuleMigrationRunner` |
| **Acceptance** | Same migration id different checksum fails; applied skipped; backup verify fail aborts; no ad-hoc SQL API public |
| **Tests** | Checksum mismatch; skip applied; ordering; restore smoke when FB present (else mocked process wrapper) |
| **Failure cases** | gbak missing, password missing, DB locked, partial DDL |
| **Depends on** | 007B1; **coordinate** with 008 schema decisions (ledger column mapping) — soft dependency, không sửa 008 |

### 007B4 – Startup activation + health-check / rollback

| | |
|---|---|
| **Goal** | Wire pipeline in `App` OnStartup before login; full matrix backup→migrate→load→health→commit/rollback; audit events |
| **Allowed files** | `App.xaml.cs`, Infrastructure pipeline orchestrator, audit event names, tests end-to-end staging folder |
| **Do not modify** | 007A installer internals; 007C UI; Supplier module |
| **Contracts** | `IModuleActivationPipeline`, `IModuleHealthCheck` |
| **Acceptance** | Pending activates on restart; failure restores previous/built-in; OperationCanceled cleanup; lock single instance |
| **Tests** | Happy path; migration fail restore; load fail quarantine; crash state Activating recovery |
| **Failure cases** | Toàn bộ matrix mục 8 |
| **Depends on** | 007B1+B2+B3 |

### 007C – Import / Update UI (ngoài 007B code, chỉ nêu)

| | |
|---|---|
| **Goal** | Màn hình chọn `.kazuko-module`, tiến trình validate/stage, Accept activation, thông báorestart; không bypass chữ ký |
| **Allowed files** | Module Settings/Update views, ViewModels, App navigation hook |
| **Do not modify** | Crypto policy, activation pipeline core |
| **Depends on** | 007B4 |

---

## 15. Startup sequence (canonical)

```text
1. ConfigureModulePaths()
2. AcquireActivationLock()                              // fail → Exit
3. LoadActivationState()                                // repair if needed
4. if RollbackPending: CompleteRollback()               // DB+DLL
5. foreach pending in activationQueue (stable order):
     state = Activating
     ReVerifyInstalled(module, version)
     Backup + Verify → backupId
     ApplyMigrations(signed only)
     Point Previous = old Active (or BUILTIN)
     Point Active = new version
     Load ALC + RegisterServices/Navigation (isolated staging registry then commit)
     HealthCheck
     if OK: state=Active; audit ACTIVATED
     else: Restore DB if needed; Active=Previous; Quarantine new; state=RolledBack; audit
6. Register remaining built-ins not overridden
7. Appearance.Apply
8. ShowLogin
```

Registration strategy an toàn: build `ModuleNavigationRegistry` tạm / hoặc đăng ký sau khi mọi module load OK — tránh nửa registry khi fail giữa chừng. Khuyến nghị: **load+validate tất cả trước**, rồi register vào registry thật một lượt.

---

## 16. Remaining questions

1. **Override Customers bằng package:** bật ngay trong B4 hay chỉ Warehouse-first? (Khuyến nghị: policy cho phép, chưa ship package Customers.)
2. **Migration ID space:** thống nhất với legacy integer trong `SCHEMA_MIGRATIONS` vs scoped string `module:...` — cần đọc DDL thật ở B3.
3. **Active pointer:** directory junction vs `version.txt` — junction mạnh nhưng cần quyền; khuyến nghị `version.txt` + resolve path (đơn giản hơn trên Windows userland).
4. **Trusted keys location:** chỉ ProgramData file hay embed + overlay?
5. **Health-check module API:** bắt buộc `IModuleHealthCheck` trên mọi module hay host-only smoke (resolve nav key + create view once)?
6. **TASK-008:** khi có migration Supplier, chạy như host migration hay đợi module package Suppliers? (Plan 008 đang BLOCKED schema — 007B ledger phải trung lập.)
7. **Multi-instance terminal UX:** exit code vs modal trước login?

---

## 17. Out of scope (xác nhận)

- Không viết code trong lần tạo tài liệu này  
- Không sửa `.cs` / `.xaml` / `.csproj` / `.sln`  
- Không sửa database / apply migration  
- Không commit  
- Không bắt đầu Supplier / TASK-008 implementation  
- Không thay đổi hành vi TASK-007A  
- Không làm Import UI (007C)

---

## Recommended architecture

Cold-start activation pipeline trên catalog versioned (`Installed`/`Active`/`Previous`/`Quarantine`) + atomic `activation-state.json`, tái sử dụng staging đã ký từ 007A; migration chỉ từ signed payload qua ledger `SCHEMA_MIGRATIONS`; rollback ưu tiên `gbak` restore + DLL pointer về previous/built-in; ALC non-collectible với shared host assemblies.

## State transition table

Xem mục 3 (From/Event/To). Các trạng thái: Staged → PendingActivation → Activating → Active | Failed/RollbackPending → RolledBack | Quarantined.

## Directory layout

Xem mục 2 (`%ProgramData%\Kazuko ERP\modules\...` + backups dưới `data\backups`).

## Startup sequence

Xem mục 16 / 15: lock → repair state → rollback pending → activate queue → built-in fill → appearance → login.

## Failure/rollback matrix

Xem mục 8.

## Public contracts đề xuất

Xem mục 13 (`IModuleLayoutPaths`, `IModuleActivationCatalog`, `IModuleActivationPipeline`, `IModuleAssemblyLoader`, `IFirebirdBackupService`, migration ledger/runner; giữ nguyên installer/manifest v2).

## Task breakdown B1–B4

Xem mục 14 (+ 007C UI tách riêng).

## Remaining questions

Xem mục 16.

## Implementation-ready

**YES** — đủ để triển khai 007B1 ngay (catalog/state) mà không chờ 007C; 007B3 cần một buổi đối chiếu DDL `SCHEMA_MIGRATIONS`/`BACKUP_HISTORY` lúc code nhưng không chặn thiết kế. Điều chỉnh nhỏ có thể phát sinh từ câu hỏi mục 16 (migration ID space, Active pointer mechanism) nhưng không đổi kiến trúc tổng thể.
