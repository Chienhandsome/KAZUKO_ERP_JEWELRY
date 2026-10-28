# Rà các ô thập phân — 28/10/2026

Không migration, không sửa dữ liệu/chứng từ đã lưu. Giữ thay đổi nghiệp vụ khác trong working tree.

## Kết quả theo màn hình

| Nơi nhập | Trước sửa | Sau sửa |
|---|---|---|
| Mẫu LSX: TL vàng ước tính | NumberStyles.Number/vi-VN đọc `0.023` thành 23; xem trước và ApplyTo đều ảnh hưởng | WeightDecimalInput cho cả preview và lưu, giữ đủ 4 số lẻ khi mở lại |
| LSX: số lượng giao NVL và sửa dòng giao | Converter vi-VN + formatter ngăn nghìn có thể đọc sai dấu chấm | PlainDecimalConverter, bỏ formatter ngăn nghìn |
| Kho: nhập/xuất NVL | Dùng chung converter/formatter với nguy cơ như giao NVL | Số thập phân thuần, cả chấm/phẩy, không phân nhóm |
| Công nợ: số chỉ thợ trả | Parse vi-VN trước invariant; formatter có thể biến `0.023` thành 23 | WeightDecimalInput, không formatter ngăn nghìn |
| Kiểm kê: số thực tế | Parse trực tiếp NumberStyles.Number/vi-VN | QuantityDecimalInput, kiểm tra nhóm đúng, nhận `0.023` đúng |
| Tem/bill: vị trí/kích thước, font, góc | Binding double mặc định WPF và StringFormat | PlainDecimalConverter độc lập OS; converter có trong tài nguyên designer để mở dialog độc lập |
| Bán/mua vàng cũ: tổng/đá, giá trị % | SalesDecimalText vốn đã chuẩn hóa một dấu phẩy/chấm rồi parse invariant không AllowThousands | Giữ nguyên; chạy lại test Sales |
| Nhập thành phẩm/hao hụt | Đã dùng WeightDecimalInput | Giữ quy tắc; parser từ chối quá 4 số lẻ |
| Offset máy in tem | Đã chọn parser dấu phẩy/chấm không AllowThousands | Giữ nguyên; test Kho bao phủ |
| Tiền VND, năm/tháng, số ca chuẩn nguyên | Là quy tắc nhập riêng, không phải ô trọng lượng | Không đổi ý nghĩa sang số thập phân thuần |

Converter số lượng dùng chung cũng được sửa: không fallback loại bỏ dấu/ký tự, không tự biến số âm/chữ khi dán thành số dương. Vẫn bảo toàn quy tắc lượng có phân nhóm `1.250` = 1250 và `1.250,5` = 1250,5; ô trọng lượng thuần `1.250` = 1,25. Không dùng chung formatter giữa hai loại ô.

## Kiểm tra tự động

- Shared.UI/DecimalInputTests: en-US, vi-VN, de-DE, fr-FR; parser, converter decimal/double và TextBox TwoWay/paste thật. Kiểm tra `0.5`, `0,5`, `0.023`, `0,023`, `0.0001`, `1000.000`, nhóm Việt Nam; từ chối dấu hỗn hợp/chữ/quá 4 số lẻ. Đầu vào hỏng của PositiveQuantity không giữ source cũ để ghi nhầm.
- Production/ProductionWeightInputTests: quote và ApplyTo nhận .023 như nhau trên 4 culture; mở lại giữ .023.
- Debt/GoldRepaymentInputTests: parser trả nợ giữ .023, từ chối nhóm mơ hồ trên 4 culture.
- Warehouse/StocktakeTests: số đếm .5/.023 với hai dấu; trường hợp `1.250,5` cũ vẫn hoạt động. Bộ Kho còn kiểm tra nhập thành phẩm, offset và tem.
- Sales: chạy bộ kiểm tra sẵn có, gồm dựng designer ở Light/Dark và font/kích thước khác nhau.

## Thử trên app

1. Nhập hoặc dán `0.5` và `0.023` lần lượt vào các ô trọng lượng; kết quả phải là nửa chỉ và 0,023 chỉ.
2. Với TL tổng 0,5 và đá 0,023: TL vàng phải 0,477; xem trước và dữ liệu đưa vào phiếu phải trùng.
3. Giao NVL rồi sửa số lượng ngay trên dòng bảng: `0.023` không thành 23.
4. Trả nợ .023 chỉ: không bị từ chối vì hiểu nhầm là 23 chỉ vượt nợ.
5. Designer: X/rộng/cỡ chữ `0.5` và `0,5` cùng cách đọc; giới hạn tối thiểu/snap của designer vẫn áp dụng bình thường.
6. Chuỗi `1.250,5` không được dùng trong ô trọng lượng thuần; ô kiểm kê có phân nhóm vẫn chấp nhận. Thử dữ liệu trên DB riêng; không tạo chứng từ thử trên DB vận hành.
