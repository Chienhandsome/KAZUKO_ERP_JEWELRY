# Hướng dẫn Vận hành Firebird Embedded 5.0.4

Tài liệu này cung cấp hướng dẫn kỹ thuật chi tiết về việc tích hợp, bố trí tệp và vận hành động cơ cơ sở dữ liệu Firebird Embedded 5.0.4 (.NET 8 Windows x64) cho môi trường phát triển (development) và môi trường khách hàng (production deployment) của hệ thống KAZUKO ERP JEWELRY.

---

## 1. Phân định Trạng thái Hiện thực

Nhằm bảo đảm tính minh bạch trong quá trình phát triển và tránh việc thông tin tài liệu bị lỗi thời khi các nhánh tác vụ được sáp nhập, các thành phần liên quan đến cơ sở dữ liệu được phân định như sau:

| Nhóm thành phần | Trạng thái hiện tại | Mô tả chi tiết |
| :--- | :--- | :--- |
| **WPF Repository** | Đang phát triển (Tham chiếu `TASK-001`) | Khung ứng dụng WPF, MVVM cơ sở và theme đã hoàn thành. `TASK-001` đang trong tiến trình thực hiện (in progress) để tích hợp xác thực và kết nối Firebird. **Người đọc cần kiểm tra trạng thái hiện tại của `TASK-001` trong `.ai/tasks/`** để xác định chính xác liệu cơ chế cấu hình copy runtime hoặc kết nối cơ sở dữ liệu đã được merge vào nhánh chính hay chưa trước khi đưa ra các giả định kỹ thuật. |
| **Tệp người dùng / Dev chuẩn bị** | Cung cấp thủ công ngoài Git | Động cơ `Firebird-5.0.4.1812-0-windows-x64` tại thư mục `firebird/` và tệp cơ sở dữ liệu ban đầu `data/data.fdb` do lập trình viên/quản trị viên chuẩn bị cục bộ trên máy, không commit vào kho mã nguồn Git. |
| **Bộ cài & Deployment** | Bố cục mục tiêu (Chưa hiện thực) | Dự án hiện tại **chưa lựa chọn hoặc hiện thực công cụ đóng gói release (installer)**. Bố cục sản phẩm được mô tả trong tài liệu này là bố cục mục tiêu (target layout) phục vụ định hướng kiến trúc, không phải là một quy trình cài đặt cố định đã triển khai. |

---

## 2. Nguyên tắc Đóng gói Runtime Firebird Embedded 5.0.4

Firebird 5.0.4 Embedded chạy dưới dạng thư viện liên kết động nội tiến trình (in-process) nạp trực tiếp vào không gian tiến trình `KAZUKO_ERP_JEWELRY.exe` (x64). Khác với các phiên bản cũ nạp một file DLL đơn lẻ, Firebird 5.0 sử dụng kiến trúc module hóa plugin.

### 2.1. Nguyên tắc cốt lõi: Bảo tồn toàn bộ gói (Complete Bundle Rule)

> [!IMPORTANT]
> **Nguyên tắc vận hành chuẩn được hỗ trợ:** Luôn **bảo tồn và phân phối toàn bộ gói (complete bundle)** Firebird 5.0.4 x64 do người dùng/nhà phát triển cung cấp.
> Không tự ý lọc bỏ các tệp nhị phân riêng lẻ, vì danh mục plugin và các thư viện phụ thuộc có thể thay đổi tùy theo bản phân phối, cấu hình bảo mật hoặc mục tiêu triển khai cụ thể.

### 2.2. Các thành phần chính thường gặp trong gói bundle

Dưới đây là các nhóm tệp và thư mục cốt lõi cần lưu ý khi kiểm tra tính toàn vẹn của gói runtime:

- **Thư viện giao tiếp gốc & cấu hình**:
  - `fbclient.dll`: Thư viện giao tiếp client/embedded của Firebird 5.0.4 (bản 64-bit).
  - `firebird.conf`: Tệp cấu hình tham số hoạt động của engine (bộ nhớ cache, lock manager, temp directories, v.v.).
  - `plugins.conf`: Khai báo ánh xạ các plugin chức năng cho Firebird engine.
  - `databases.conf`: Cấu hình danh mục alias cơ sở dữ liệu.
  - `firebird.msg`: Tệp thông điệp lỗi và cảnh báo đa ngôn ngữ của Firebird.
- **Thư mục Plugin Động cơ (`plugins/`)**:
  - Chứa `engine13.dll` (động cơ thực thi truy vấn chính), các module xác thực (`srp.dll`, `legacy_auth.dll`), module mã hóa (`chacha.dll`), UDR engine (`udr_engine.dll`) và profiler/trace.
- **Thư mục Quốc tế hóa và Mã hóa Ký tự (`intl/`)**:
  - Chứa `fbintl.dll` và `fbintl.conf`. Đây là thành phần bắt buộc để xử lý đúng tập ký tự UTF-8 và collation tiếng Việt có dấu.
- **Thư mục Dữ liệu Múi giờ (`tzdata/`)**:
  - Chứa `zoneinfo64.res`, `windowsZones.res`, `metaZones.res`, `timezoneTypes.res`, `ids.dat` phục vụ kiểu dữ liệu thời gian có múi giờ (`TIMESTAMP WITH TIME ZONE`).
- **Thư viện ICU và C++ Runtime**:
  - Các tệp thư viện ICU: `icudt63.dll`, `icudt63l.dat`, `icuin63.dll`, `icuuc63.dll`.
  - Các thư viện C++ runtime và nén: `msvcp140.dll`, `vcruntime140.dll`, `zlib1.dll`, `ib_util.dll`.
- **Tiện ích bảo trì (Khuyến nghị)**:
  - `gbak.exe`: Tiện ích sao lưu và phục hồi cơ sở dữ liệu chuẩn của Firebird.

---

## 3. Bố cục Thư mục Đề xuất (Folder Layouts)

### 3.1. Bố cục Môi trường Phát triển (Development Layout)

Trong môi trường làm việc của lập trình viên, mã nguồn tách rời khỏi dữ liệu thực tế và runtime binary:

```text
D:\Working\KAZUKO_ERP_JEWELRY\
├── firebird\
│   └── Firebird-5.0.4.1812-0-windows-x64\   <-- Gói Firebird bundle đầy đủ do lập trình viên chuẩn bị
│       ├── fbclient.dll
│       ├── firebird.conf
│       ├── firebird.msg
│       ├── plugins.conf
│       ├── gbak.exe
│       ├── icu*.dll, icudt63l.dat, vcruntime*.dll
│       ├── plugins\ (engine13.dll, srp.dll, ...)
│       ├── intl\    (fbintl.dll, fbintl.conf)
│       └── tzdata\  (*.res, ids.dat)
├── data\
│   └── data.fdb                             <-- Database thử nghiệm cục bộ (ngoài Git)
├── src\
│   ├── Kazuko.App\                          <-- Dự án host WPF (Assembly: KAZUKO_ERP_JEWELRY)
│   │   └── bin\Debug\net8.0-windows\        <-- Thư mục output khi biên dịch Debug
│   │       ├── KAZUKO_ERP_JEWELRY.exe       <-- File thực thi chính của ứng dụng
│   │       ├── firebird\                    <-- Runtime copy tự động khi build (nếu có cấu hình)
│   │       │   ├── fbclient.dll
│   │       │   ├── plugins\
│   │       │   ├── intl\
│   │       │   └── tzdata\
│   │       └── data\
│   │           └── data.fdb                 <-- Bản sao dữ liệu chạy debug
│   ├── Kazuko.Core\
│   ├── Kazuko.Infrastructure\
│   └── ...
```

#### Quy tắc Sao chép Database trong Phát triển (Development Database-Copy Rule)
- **Tuyệt đối không sử dụng thiết lập ghi đè bắt buộc (`CopyToOutputDirectory = Always`)** đối với tệp `data.fdb`. Việc này sẽ làm xóa sạch toàn bộ dữ liệu mẫu, phiếu nhập xuất vừa thử nghiệm mỗi lần bấm F5/Build.
- Chỉ sao chép nếu tệp chưa tồn tại ở đích đến (`PreserveNewest` hoặc kiểm tra bằng logic khởi tạo).
- `data.fdb` và các tệp runtime nhị phân giải nén phải nằm trong `.gitignore` để tránh đẩy dữ liệu nhạy cảm hoặc phình to kho mã nguồn Git.

---

### 3.2. Bố cục Môi trường Triển khai Khách hàng (Target Production Layout)

> [!NOTE]
> Dự án chưa triển khai bộ cài đặt tự động. Bố cục dưới đây là mô hình kiến trúc mục tiêu (target layout) khuyến nghị cho các tác vụ đóng gói release trong tương lai.

Đối với máy trạm khách hàng, **bắt buộc phân tách hoàn toàn giữa thư mục nhị phân ứng dụng và thư mục dữ liệu nghiệp vụ**:

```text
[Ổ đĩa C hoặc Ổ đĩa cài đặt]
├── Program Files\
│   └── Kazuko ERP\                          <-- CHỈ ĐỌC (Read-Only đối với người dùng thường)
│       ├── KAZUKO_ERP_JEWELRY.exe           <-- File thực thi ứng dụng
│       ├── Kazuko.Core.dll
│       ├── Kazuko.Infrastructure.dll
│       ├── firebird\                        <-- Gói Firebird runtime đầy đủ
│       │   ├── fbclient.dll
│       │   ├── firebird.conf
│       │   ├── plugins\
│       │   ├── intl\
│       │   └── tzdata\
│       └── ...
│
└── ProgramData\ (C:\ProgramData\Kazuko ERP\)  <-- GHI CÓ ĐẶC QUYỀN TỐI THIỂU
    ├── data\
    │   ├── data.fdb                         <-- Tệp cơ sở dữ liệu hoạt động chính
    │   └── backups\                         <-- Thư mục chứa các tệp sao lưu gbak định kỳ
    └── logs\                                <-- Ghi log sự kiện hệ thống
```

#### Nguyên tắc phân quyền và an toàn dữ liệu:
1. **Nguyên tắc Đặc quyền Tối thiểu (Least-Privilege Write Access)**:
   - Thư mục dữ liệu `C:\ProgramData\Kazuko ERP\data\` **chỉ cấp quyền ghi/sửa (Read/Write/Modify) cho nhóm người dùng hoặc tài khoản Windows cục bộ được ủy quyền vận hành phần mềm** (hoặc service account thực thi tác vụ).
   - **Tuyệt đối không cấp quyền mở rộng (Full Control) cho tất cả mọi người (`Everyone` / broad Users)** để tránh rủi ro xóa nhầm hoặc xâm nhập trái phép vào tệp cơ sở dữ liệu kim hoàn.
2. **Kiểm soát UAC (User Account Control)**:
   - Thư mục `C:\Program Files` được Windows bảo vệ nghiêm ngặt. Ứng dụng chạy với quyền người dùng tiêu chuẩn không thể mở khóa ghi (read-write) vào file nằm trong `Program Files`.
3. **Tránh lỗi UAC VirtualStore**:
   - Nếu cố tình ghi vào `Program Files`, Windows UAC sẽ âm thầm chuyển hướng tệp sang thư mục ảo `%LOCALAPPDATA%\VirtualStore\Program Files\...`, khiến dữ liệu bị phân mảnh và sai lệch giữa các tài khoản Windows trên cùng một máy.
4. **An toàn khi nâng cấp**:
   - Khi cập nhật phiên bản mới, bộ cài chỉ ghi đè thư mục ứng dụng trong `Program Files` mà không can thiệp hay làm mất tệp `data.fdb` trong `ProgramData`.

---

## 4. Hạn chế Kỹ thuật Cốt lõi của Chế độ Embedded (Single-Host vs. Multi-Machine)

### 4.1. Cơ chế Hoạt động của Chế độ Embedded
Trong chế độ Embedded:
- Tiến trình ứng dụng `KAZUKO_ERP_JEWELRY.exe` trực tiếp tải `fbclient.dll` và `plugins/engine13.dll` vào không gian bộ nhớ của chính nó.
- Động cơ cơ sở dữ liệu chạy nội tiến trình, đọc ghi trực tiếp tệp vật lý `data.fdb` thông qua API tệp của hệ điều hành.
- Quản lý khóa (Lock Manager) sử dụng bộ nhớ chia sẻ cục bộ (local shared memory/IPC) của hệ điều hành Windows trên máy đó.

### 4.2. Hạn chế Nghiêm ngặt: Không dùng chung file qua Mạng (SMB / Network Share)
- **Tuyệt đối không chia sẻ thư mục chứa `data.fdb` qua mạng LAN (Windows Shared Folder, SMB, NFS, Google Drive, Dropbox, OneDrive) để nhiều máy tính cùng mở bằng Firebird Embedded.**
- **Hậu quả**:
  - Cơ chế khóa nội bộ của Firebird không thể đồng bộ qua giao thức chia sẻ file mạng giữa các máy trạm độc lập.
  - Ngay khi máy thứ hai cố gắng mở tệp, hệ điều hành sẽ báo lỗi khóa tệp (`file is locked by another process`).
  - Trong trường hợp cơ chế khóa file mạng bị bỏ qua hoặc trễ nhịp, việc ghi đè đồng thời các trang đĩa (pages) từ nhiều máy tính sẽ dẫn đến **hỏng vĩnh viễn cấu trúc dữ liệu vật lý (Database Corruption)** và mất toàn bộ dữ liệu.
- **Phạm vi áp dụng**: Chế độ Embedded **chỉ dùng cho một máy tính duy nhất (Standalone Single-Host)**.

### 4.3. Giải pháp khi Cần Nhiều Máy Trạm Sử dụng Chung Dữ liệu (Firebird Server)
Khi doanh nghiệp có từ 2 máy trạm trở lên cùng truy cập vào một cơ sở dữ liệu:
- **Bắt buộc cài đặt Firebird Server 5.0 (Dedicated Service)** trên một máy chủ (hoặc máy trạm đóng vai trò máy chủ trong mạng LAN).
- Dịch vụ `firebird.exe` trên máy chủ sẽ là tiến trình duy nhất mở tệp `data.fdb` và điều phối lock table tập trung.
- Các máy trạm client (chạy ứng dụng KAZUKO ERP WPF) sẽ chuyển chế độ kết nối từ `Embedded` sang `Server`:
  - Kết nối qua giao thức TCP cổng `3050`.
  - Cấu hình chuỗi kết nối:
    ```text
    ServerType=Default; DataSource=192.168.1.100; Port=3050; Database=D:\KazukoData\data.fdb; UserID=...; Password=...; Charset=UTF8;
    ```

---

## 5. Bảng kiểm Xử lý Sự cố Thường gặp (Troubleshooting Checklist)

Khi ứng dụng gặp sự cố kết nối cơ sở dữ liệu Firebird Embedded, hãy rà soát tuần tự theo bảng kiểm sau:

| Hiện tượng / Mã lỗi | Nguyên nhân gốc rễ | Cách kiểm tra và khắc phục |
| :--- | :--- | :--- |
| **`Unable to load DLL 'fbclient.dll'`** hoặc `DllNotFoundException` | Không tìm thấy `fbclient.dll` tại đường dẫn chỉ định hoặc thiếu các DLL phụ thuộc. | 1. Kiểm tra thuộc tính `ClientLibrary` trong chuỗi kết nối / cấu hình runtime.<br>2. Đảm bảo toàn bộ gói Firebird runtime được copy đầy đủ vào thư mục `firebird/` cạnh file thực thi.<br>3. Kiểm tra xem các file `icudt63.dll`, `icuin63.dll`, `icuuc63.dll` có nằm cùng thư mục với `fbclient.dll` hay không. |
| **`BadImageFormatException`** (0x8007000B) | Lệch kiến trúc vi xử lý (Architecture Mismatch): Tiến trình ứng dụng chạy x64 nhưng nạp DLL x86 (hoặc ngược lại). | Dự án KAZUKO ERP được xây dựng nhắm mục tiêu `win-x64` (`PlatformTarget: x64`). Bắt buộc toàn bộ các file DLL trong thư mục Firebird phải là bản **64-bit**. Không được copy lẫn các tệp từ bản Firebird 32-bit (x86). |
| **`operating system directive CreateFile failed: Access is denied`** | Quyền hạn truy cập tệp/thư mục cơ sở dữ liệu không đủ. | Tài khoản người dùng Windows đang chạy ứng dụng không có quyền ghi vào tệp `.fdb`. Chuột phải vào thư mục chứa dữ liệu (ví dụ `C:\ProgramData\Kazuko ERP\data\`) -> Properties -> Security -> Cấp quyền `Modify` theo nguyên tắc đặc quyền tối thiểu cho nhóm/người dùng được ủy quyền. |
| **Dữ liệu bị "quay về quá khứ" hoặc biến mất sau khi đóng app** | Tệp cơ sở dữ liệu được đặt trong `C:\Program Files` và bị Windows UAC kích hoạt cơ chế `VirtualStore`. | Tuyệt đối không đặt đường dẫn file database trỏ vào `Program Files`. Hãy di chuyển tệp `data.fdb` ra `C:\ProgramData\Kazuko ERP\data\` hoặc ổ đĩa dữ liệu riêng (`D:\KazukoData\`). |
| **Báo thiếu `VCRUNTIME140.dll` / `MSVCP140.dll` hoặc lỗi khởi động C++ runtime** | Máy trạm khách hàng chưa có thư viện Visual C++ Runtime và gói bundle thiếu DLL tương ứng. | **Kiểm tra có điều kiện**: Nếu gói Firebird bundle chưa tích hợp sẵn các file DLL runtime (`vcruntime140.dll`, `msvcp140.dll`), hoặc hệ thống Windows gặp lỗi phụ thuộc, hãy cài đặt gói **Microsoft Visual C++ 2015–2022 Redistributable (x64)** chính thức trên máy trạm khách hàng. |
| **Lỗi `Engine13` không nạp được hoặc thông báo plugin thất bại** | Thiếu thư mục con `plugins\` hoặc tệp `plugins.conf` bị mất / sai đường dẫn. | Đảm bảo bảo tồn đầy đủ gói bundle gốc: thư mục con `plugins/` phải nằm ngay cạnh `fbclient.dll` và chứa đầy đủ `engine13.dll`, `srp.dll`, `legacy_auth.dll`. Không đổi tên hay xóa tệp `plugins.conf`. |
| **Lỗi hiển thị ký tự Unicode tiếng Việt hoặc lỗi Transliteration** | Thiếu thư viện quốc tế hóa trong thư mục `intl/`. | Đảm bảo thư mục `intl/` chứa `fbintl.dll` và `fbintl.conf` nằm đầy đủ trong thư mục `firebird/`. Trong cấu hình chuỗi kết nối luôn đặt `Charset = UTF8`. |
| **Lỗi `file ... is in use by another process`** | Có tiến trình khác đang mở tệp `data.fdb` ở chế độ độc quyền. | 1. Kiểm tra xem có một tiến trình `KAZUKO_ERP_JEWELRY.exe` khác đang chạy ngầm trong Task Manager hay không.<br>2. Kiểm tra xem có công cụ quản trị (như FlameRobin, DBeaver, ISQL) nào đang kết nối trực tiếp dạng Embedded vào tệp hay không. Đóng các tiến trình đó trước khi khởi động lại ứng dụng. |

---

## 6. Nguyên tắc An toàn và Bảo mật Vận hành

1. **Bảo mật Thông tin Đăng nhập & Kết nối**:
   - Không lưu trữ mật khẩu kết nối dạng văn bản rõ (cleartext) trong mã nguồn hoặc tệp cấu hình JSON công khai.
   - Khi chuyển sang chế độ Server trong tương lai, mật khẩu kết nối của máy trạm phải được bảo vệ bằng Windows DPAPI (`ProtectedData`) trước khi lưu xuống `%LOCALAPPDATA%`.
2. **Quy trình Sao lưu Dự phòng (Backup)**:
   - Trong chế độ Embedded, việc sao chép file trực tiếp (`Copy File`) khi ứng dụng đang mở có nguy cơ tạo ra bản sao lưu không toàn vẹn (dirty read).
   - Quy trình sao lưu khuyến nghị: Sử dụng tiện ích `gbak.exe` của Firebird chạy dòng lệnh sao lưu trực tiếp sang tệp nén `.fbk`, hoặc chỉ copy tệp `data.fdb` sau khi đã đóng hoàn toàn ứng dụng KAZUKO ERP.
3. **Độc lập Cấu hình**:
   - Mã nguồn ứng dụng tuyệt đối không hard-code đường dẫn tuyệt đối của máy lập trình viên (ví dụ `D:\Working\...`) vào logic kết nối. Đường dẫn cơ sở dữ liệu phải được giải quyết linh hoạt dựa theo thư mục dữ liệu chuẩn của hệ điều hành (`Environment.SpecialFolder.CommonApplicationData`) hoặc cấu hình người dùng.
