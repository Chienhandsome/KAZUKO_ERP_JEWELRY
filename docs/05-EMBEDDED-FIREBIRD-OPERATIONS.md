# Hướng dẫn Vận hành Firebird Embedded 5.0.4

Tài liệu này cung cấp hướng dẫn kỹ thuật chi tiết về việc tích hợp, bố trí tệp và vận hành động cơ cơ sở dữ liệu Firebird Embedded 5.0.4 (.NET 8 Windows x64) cho môi trường phát triển (development) và môi trường khách hàng (production deployment) của hệ thống KAZUKO ERP JEWELRY.

---

## 1. Phân định Trạng thái Hiện thực

Nhằm bảo đảm tính minh bạch trong quá trình phát triển theo tài liệu kiến trúc, các thành phần liên quan đến cơ sở dữ liệu được phân định rõ ràng như sau:

| Nhóm thành phần | Trạng thái hiện tại | Mô tả chi tiết |
| :--- | :--- | :--- |
| **WPF Repository** | Đang phát triển (chưa có code runtime Firebird trên nhánh chính) | Repository hiện tại mới có khung ứng dụng WPF, MVVM cơ sở, theme và module Login mock (`DevelopmentAuthenticationService`). `TASK-001` đang tiến hành tích hợp xác thực qua Firebird. Repository chưa cấu hình tự động copy binary Firebird hoặc tự tạo database. |
| **Tệp người dùng / Dev chuẩn bị** | Cung cấp thủ công ngoài Git | Động cơ `Firebird-5.0.4.1812-0-windows-x64` tại thư mục `firebird/` và tệp cơ sở dữ liệu ban đầu `data/data.fdb` do lập trình viên/quản trị viên chuẩn bị cục bộ trên máy, không commit vào kho mã nguồn Git. |
| **Bộ cài & Deployment** | Định hướng tương lai (chưa hiện thực) | Kế hoạch đóng gói bộ cài (Inno Setup / WiX / MSIX) cùng cơ chế tự động giải nén runtime, cấp quyền thư mục `ProgramData` và cấu hình chuỗi kết nối cho khách hàng. |

---

## 2. Danh mục Tệp Runtime Bắt buộc (Required Runtime Files)

Firebird 5.0.4 Embedded chạy dưới dạng thư viện liên kết động nội tiến trình (in-process) nạp trực tiếp vào không gian tiến trình `Kazuko.App.exe` (x64). Khác với các phiên bản cũ (như Firebird 2.5 nạp một file đơn lẻ), Firebird 5.0 sử dụng kiến trúc module hóa plugin. Khi nạp `fbclient.dll`, động cơ sẽ tìm kiếm và tải các plugin engine, bộ giải mã ký tự và dữ liệu múi giờ từ các thư mục con tương ứng.

Dưới đây là danh mục các tệp tối thiểu bắt buộc phải có mặt trong thư mục runtime `firebird/`:

### 2.1. Thư viện cốt lõi và tệp cấu hình gốc

- `fbclient.dll`: Thư viện giao tiếp client/embedded của Firebird 5.0.4 (bản 64-bit).
- `firebird.conf`: Tệp cấu hình tham số hoạt động của engine (bộ nhớ cache, lock manager, temp directories, v.v.).
- `plugins.conf`: Khai báo ánh xạ các plugin chức năng cho Firebird engine.
- `databases.conf`: Cấu hình danh mục alias cơ sở dữ liệu (tùy chọn nhưng cần thiết khi định tuyến bí danh).
- `firebird.msg`: Tệp thông điệp lỗi và cảnh báo đa ngôn ngữ của Firebird.

### 2.2. Thư mục Plugin Động cơ (`firebird/plugins/`)

Động cơ Firebird 5 Embedded không thể thực thi truy vấn nếu thiếu các plugin này:
- `engine13.dll`: Động cơ lưu trữ và thực thi truy vấn SQL chính của Firebird 5 (Storage & Execution Engine).
- `srp.dll`: Plugin xác thực người dùng giao thức SRP (Secure Remote Password).
- `legacy_auth.dll`: Plugin hỗ trợ phương thức xác thực chuẩn cũ.
- `legacy_usermanager.dll`: Quản lý tài khoản người dùng theo chuẩn cũ.
- `chacha.dll`: Module mã hóa mật khẩu/giao tiếp.
- `udr_engine.dll` & `udr_engine.conf`: Hỗ trợ User Defined Routines.
- `default_profiler.dll`, `fbtrace.dll`: Module giám sát và ghi log dấu vết (trace).

### 2.3. Thư mục Quốc tế hóa và Mã hóa Ký tự (`firebird/intl/`)

Đặc biệt quan trọng đối với ứng dụng kim hoàn hiển thị tiếng Việt UTF-8:
- `fbintl.dll`: Thư viện xử lý tập ký tự (charset collation) quốc tế.
- `fbintl.conf`: Cấu hình bảng mã và sắp xếp thứ tự Unicode (UTF8, WIN1258). Thiếu tệp này sẽ dẫn đến lỗi không thể chuyển đổi hoặc truy vấn chuỗi tiếng Việt có dấu.

### 2.4. Thư mục Dữ liệu Múi giờ (`firebird/tzdata/`)

Cần thiết cho việc lưu trữ các cột kiểu `TIMESTAMP WITH TIME ZONE` của Firebird 5:
- `zoneinfo64.res`, `windowsZones.res`, `metaZones.res`, `timezoneTypes.res`, `ids.dat`: Dữ liệu phân giải múi giờ IANA / Windows.

### 2.5. Các Thư viện Phụ thuộc (ICU và C++ Runtime)

Phải đặt cùng thư mục với `fbclient.dll` (hoặc có sẵn trong hệ thống):
- **Thư viện ICU**: `icudt63.dll`, `icudt63l.dat`, `icuin63.dll`, `icuuc63.dll` (xử lý đối chiếu chuỗi Unicode).
- **Thư viện tiện ích**: `ib_util.dll`, `zlib1.dll`.
- **Microsoft Visual C++ Redistributable (x64)**: `msvcp140.dll`, `msvcp140_1.dll`, `vcruntime140.dll`, `vcruntime140_1.dll`. Máy trạm cần cài sẵn gói VC++ 2015–2022 Redistributable x64, hoặc các file DLL này phải nằm cùng thư mục nhị phân.

### 2.6. Tiện ích Bảo trì Bổ trợ (Khuyến nghị)

- `gbak.exe`: Công cụ sao lưu (backup) và phục hồi (restore) cấu trúc, dữ liệu chuẩn của Firebird. Được sử dụng cho tác vụ bảo trì tự động hoặc phục hồi sự cố.

---

## 3. Bố cục Thư mục Đề xuất (Folder Layouts)

### 3.1. Bố cục Môi trường Phát triển (Development Layout)

Trong môi trường làm việc của lập trình viên, mã nguồn tách rời khỏi dữ liệu thực và runtime binary tải về:

```text
D:\Working\KAZUKO_ERP_JEWELRY\
├── firebird\
│   └── Firebird-5.0.4.1812-0-windows-x64\   <-- Runtime Firebird do lập trình viên tải
│       ├── fbclient.dll
│       ├── firebird.conf
│       ├── firebird.msg
│       ├── plugins.conf
│       ├── gbak.exe
│       ├── icu*.dll, icudt63l.dat, vcruntime*.dll
│       ├── plugins\ (chứa engine13.dll, srp.dll, ...)
│       ├── intl\    (chứa fbintl.dll, fbintl.conf)
│       └── tzdata\  (chứa *.res, ids.dat)
├── data\
│   └── data.fdb                             <-- Database thử nghiệm gốc (không commit Git)
├── src\
│   ├── Kazuko.App\
│   ├── Kazuko.Core\
│   ├── Kazuko.Infrastructure\
│   └── ...
└── bin\Debug\net8.0-windows\win-x64\         <-- Output khi biên dịch
    ├── Kazuko.App.exe
    ├── firebird\                            <-- Đóng gói/copy runtime
    │   ├── fbclient.dll
    │   ├── plugins\
    │   ├── intl\
    │   └── tzdata\
    └── data\
        └── data.fdb                         <-- Bản sao dữ liệu chạy debug
```

#### Quy tắc Sao chép Database trong Phát triển (Development Database-Copy Rule)
- **Tuyệt đối không sử dụng thiết lập ghi đè bắt buộc (`CopyToOutputDirectory = Always`)** đối với tệp `data.fdb`. Việc này sẽ làm xóa sạch toàn bộ dữ liệu mẫu, phiếu nhập xuất vừa thử nghiệm mỗi lần bấm F5/Build.
- Chỉ sao chép nếu tệp chưa tồn tại ở đích đến (`PreserveNewest` hoặc kiểm tra bằng code khi khởi động).
- `data.fdb` và các tệp runtime giải nén phải nằm trong `.gitignore` để tránh đẩy dữ liệu nhạy cảm hoặc phình to kho mã nguồn Git.

---

### 3.2. Bố cục Môi trường Triển khai Khách hàng (Production / Release Layout)

Đối với máy trạm khách hàng, **bắt buộc phân tách hoàn toàn giữa thư mục nhị phân ứng dụng và thư mục dữ liệu nghiệp vụ**.

```text
[Ổ đĩa C hoặc Ổ đĩa cài đặt]
├── Program Files\
│   └── Kazuko ERP\                          <-- CHỈ ĐỌC (Read-Only đối với User thường)
│       ├── Kazuko.App.exe
│       ├── Kazuko.Core.dll
│       ├── Kazuko.Infrastructure.dll
│       ├── firebird\
│       │   ├── fbclient.dll
│       │   ├── firebird.conf
│       │   ├── plugins\
│       │   ├── intl\
│       │   └── tzdata\
│       └── ...
│
└── ProgramData\ (C:\ProgramData\Kazuko ERP\)  <-- GHI ĐƯỢC (Read/Write cho mọi người dùng)
    ├── data\
    │   ├── data.fdb                         <-- Tệp dữ liệu hoạt động chính
    │   └── backups\                         <-- Thư mục sao lưu gbak định kỳ
    └── logs\                                <-- Ghi log sự kiện hệ thống
```

#### Lý do bắt buộc phân tách:
1. **Kiểm soát UAC (User Account Control)**: Kể từ Windows Vista/7/10/11, thư mục `C:\Program Files` được bảo vệ nghiêm ngặt. Ứng dụng chạy với quyền người dùng tiêu chuẩn (không Run as Administrator) không thể mở khóa ghi (read-write) vào file nằm trong `Program Files`.
2. **Tránh lỗi UAC VirtualStore**: Nếu cố tình ghi vào `Program Files`, Windows sẽ âm thầm chuyển hướng tệp sang thư mục ảo `%LOCALAPPDATA%\VirtualStore\Program Files\...`. Điều này làm việc quản lý, sao lưu và đồng bộ dữ liệu của khách hàng bị sai lệch hoàn toàn giữa các tài khoản Windows trên cùng một máy.
3. **An toàn khi nâng cấp phần mềm**: Khi cài đặt bản cập nhật ứng dụng mới, bộ cài chỉ việc ghi đè thư mục `Program Files\Kazuko ERP` mà không bao giờ can thiệp hay làm mất tệp `data.fdb` trong `ProgramData`.

---

## 4. Hạn chế Kỹ thuật Cốt lõi của Chế độ Embedded (Single-Host vs. Multi-Machine)

### 4.1. Cơ chế Hoạt động của Chế độ Embedded
Trong chế độ Embedded:
- Tiến trình ứng dụng `Kazuko.App.exe` trực tiếp tải `fbclient.dll` và `plugins/engine13.dll` vào bộ nhớ RAM của chính nó.
- Động cơ cơ sở dữ liệu chạy ngay bên trong tiến trình client, đọc ghi trực tiếp tệp vật lý `data.fdb` trên ổ cứng thông qua API file của hệ điều hành.
- Quản lý khóa (Lock Manager) sử dụng bộ nhớ chia sẻ cục bộ (local shared memory/IPC) của hệ điều hành Windows trên máy đó.

### 4.2. Hạn chế Nghiêm ngặt: Không dùng chung file qua Mạng (SMB / Network Share)
- **Không bao giờ chia sẻ thư mục chứa `data.fdb` qua mạng LAN (Windows Shared Folder, SMB, NFS, Google Drive, Dropbox, OneDrive) để nhiều máy tính cùng trỏ Firebird Embedded vào.**
- **Hậu quả**:
  - Cơ chế khóa nội bộ của Firebird không thể đồng bộ qua giao thức mạng SMB giữa các máy khác nhau.
  - Ngay khi máy thứ hai cố gắng mở tệp, hệ điều hành sẽ báo lỗi khóa tệp (`file is locked by another process`).
  - Trong trường hợp xấu nhất nếu cơ chế khóa file qua mạng bị bỏ qua, các máy ghi đè trang đĩa (pages) của nhau đồng thời sẽ dẫn đến **hỏng vĩnh viễn cấu trúc dữ liệu vật lý (Database Corruption)** và mất trắng dữ liệu xưởng kim hoàn.
- **Phạm vi áp dụng của Embedded**: Chế độ Embedded **chỉ dùng cho một máy duy nhất (Standalone Single-Host)**, ví dụ máy tính cá nhân của thợ kim hoàn độc lập hoặc máy của lập trình viên.

### 4.3. Giải pháp khi Cần Nhiều Máy Trạm Sử dụng Chung Dữ liệu (Firebird Server)
Khi xưởng kim hoàn mở rộng quy mô có từ 2 máy tính trở lên cùng truy cập vào một cơ sở dữ liệu:
- **Bắt buộc cài đặt Firebird Server 5.0 (Dedicated Service)** trên một máy chủ (hoặc một máy trạm đóng vai trò máy chủ trong mạng LAN).
- Dịch vụ `firebird.exe` trên máy chủ sẽ là tiến trình duy nhất mở tệp `data.fdb` và quản lý lock table tập trung.
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
| **`Unable to load DLL 'fbclient.dll'`** hoặc `DllNotFoundException` | Không tìm thấy `fbclient.dll` tại đường dẫn chỉ định hoặc thiếu các DLL phụ thuộc. | 1. Kiểm tra thuộc tính `ClientLibrary` trong chuỗi kết nối / cấu hình runtime.<br>2. Đảm bảo `fbclient.dll` nằm đúng thư mục `firebird/` cạnh file thực thi.<br>3. Kiểm tra xem các file `icudt63.dll`, `icuin63.dll`, `icuuc63.dll` có nằm cùng thư mục với `fbclient.dll` hay không. |
| **`BadImageFormatException`** (0x8007000B) | Lệch kiến trúc vi xử lý (Architecture Mismatch): Tiến trình ứng dụng chạy x64 nhưng nạp DLL x86 (hoặc ngược lại). | Dự án KAZUKO ERP được xây dựng nhắm mục tiêu `win-x64` (hoặc `PlatformTarget: x64`). Bắt buộc toàn bộ các file DLL trong thư mục Firebird phải là bản **64-bit**. Không được copy lẫn các tệp từ bản Firebird 32-bit (x86). |
| **`operating system directive CreateFile failed: Access is denied`** | Quyền hạn truy cập tệp/thư mục cơ sở dữ liệu không đủ. | Tài khoản người dùng Windows đang chạy ứng dụng không có quyền ghi vào tệp `.fdb`. Chuột phải vào thư mục chứa dữ liệu (ví dụ `C:\ProgramData\Kazuko ERP\data\`) -> Properties -> Security -> Cấp quyền `Modify` (hoặc `Full Control`) cho nhóm `Users` hoặc người dùng hiện tại. |
| **Dữ liệu bị "quay về quá khứ" hoặc biến mất sau khi đóng app** | Tệp cơ sở dữ liệu được đặt trong `C:\Program Files` và bị Windows UAC kích hoạt cơ chế `VirtualStore`. | Tuyệt đối không đặt đường dẫn file database trỏ vào `Program Files`. Hãy di chuyển tệp `data.fdb` ra `C:\ProgramData\Kazuko ERP\data\` hoặc ổ đĩa dữ liệu riêng (`D:\KazukoData\`). |
| **Lỗi khởi động hoặc báo thiếu `VCRUNTIME140.dll` / `MSVCP140.dll`** | Máy khách hàng là Windows mới cài đặt, thiếu bộ thư viện Visual C++ Runtime. | Cài đặt gói **Microsoft Visual C++ 2015–2022 Redistributable (x64)** chính thức từ Microsoft trên máy trạm khách hàng trước khi chạy ứng dụng. |
| **Lỗi `Engine13` không nạp được hoặc thông báo plugin thất bại** | Thiếu thư mục con `plugins\` hoặc tệp `plugins.conf` bị mất / sai đường dẫn. | Đảm bảo thư mục con `plugins/` tồn tại ngay cạnh `fbclient.dll` và chứa đầy đủ `engine13.dll`, `srp.dll`, `legacy_auth.dll`. Không đổi tên hay xóa tệp `plugins.conf`. |
| **Lỗi hiển thị ký tự Unicode tiếng Việt hoặc lỗi Transliteration** | Thiếu thư viện quốc tế hóa trong thư mục `intl/`. | Đảm bảo thư mục `intl/` chứa `fbintl.dll` và `fbintl.conf` nằm đầy đủ trong thư mục `firebird/`. Trong cấu hình chuỗi kết nối luôn đặt `Charset = UTF8`. |
| **Lỗi `file ... is in use by another process`** | Có tiến trình khác đang mở tệp `data.fdb` ở chế độ độc quyền. | 1. Kiểm tra xem có một instance khác của `Kazuko.App.exe` đang chạy ngầm trong Task Manager hay không.<br>2. Kiểm tra xem có công cụ quản trị (như FlameRobin, DBeaver, ISQL) nào đang kết nối trực tiếp dạng Embedded vào tệp hay không. Đóng các tiến trình đó trước khi khởi động lại ứng dụng. |

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
