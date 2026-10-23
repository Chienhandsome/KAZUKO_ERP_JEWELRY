using System.Globalization;
using System.Text;

namespace Kazuko.Core.Customers;

public sealed record CitizenIdentityScanData(string Number, string PreviousNumber, string FullName,
    DateTime BirthDate, string Gender, string Address, DateTime? IssueDate);

public sealed record CitizenIdentityParseResult(CitizenIdentityScanData? Data, string Error)
{
    public bool Success => Data is not null;
}

public static class CitizenIdentityQrParser
{
    public static bool IsNumber(string? value, int length = 12) =>
        value is not null && value.Length == length && value.All(c => c is >= '0' and <= '9');

    public static CitizenIdentityParseResult Parse(string? raw, DateTime today)
    {
        CitizenIdentityParseResult Fail(string message) => new(null, message);
        if (string.IsNullOrWhiteSpace(raw)) return Fail("Vui lòng quét hoặc dán chuỗi QR CCCD.");
        if (raw.Length > 4096) return Fail("Chuỗi quét quá dài.");
        string value;
        try { value = raw.Trim().Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return Fail("Dữ liệu tiếng Việt không hợp lệ. Vui lòng kiểm tra chế độ máy quét."); }
        if (value.Contains('\uFFFD') || value.Any(char.IsControl))
            return Fail("Dữ liệu quét có ký tự lỗi. Vui lòng kiểm tra chế độ máy quét.");
        var parts = value.Split('|', StringSplitOptions.None).Select(part => part.Trim()).ToArray();
        if (parts.Length is not (5 or 6 or 7)) return Fail("Không nhận diện được cấu trúc QR CCCD. Vui lòng quét lại.");
        bool hasOld = parts.Length == 7;
        string number = parts[0], previous = hasOld ? parts[1] : string.Empty;
        int nameIndex = hasOld ? 2 : 1;
        string name = parts[nameIndex], birthRaw = parts[nameIndex + 1], genderRaw = parts[nameIndex + 2];
        string address = parts[nameIndex + 3];
        string issueRaw = parts.Length > nameIndex + 4 ? parts[nameIndex + 4] : string.Empty;
        if (!IsNumber(number)) return Fail("Số CCCD phải gồm đúng 12 chữ số, giữ cả số 0 đầu.");
        if (previous.Length > 0 && !IsNumber(previous, 9)) return Fail("Số CMND cũ phải gồm 9 chữ số hoặc để trống.");
        if (name.Length is 0 or > 200) return Fail("Họ tên phải có từ 1 đến 200 ký tự.");
        if (address.Length > 500) return Fail("Địa chỉ tối đa 500 ký tự.");
        if (!DateTime.TryParseExact(birthRaw, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth)
            || birth.Date > today.Date) return Fail("Ngày sinh không hợp lệ (ddMMyyyy) hoặc ở tương lai.");
        string gender = genderRaw.ToUpperInvariant() switch
        {
            "NAM" => "Nam", "NỮ" or "NU" => "Nữ", "KHÁC" or "KHAC" => "Khác", _ => string.Empty
        };
        if (gender.Length == 0) return Fail("Giới tính trong chuỗi quét không được hỗ trợ.");
        DateTime? issue = null;
        if (issueRaw.Length > 0)
        {
            if (!DateTime.TryParseExact(issueRaw, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date.Date > today.Date || date.Date < birth.Date) return Fail("Ngày cấp không hợp lệ, trước ngày sinh hoặc ở tương lai.");
            issue = date.Date;
        }
        return new(new(number, previous, name, birth.Date, gender, address, issue), string.Empty);
    }
}

public sealed record CustomerIdentityMatch(long Id, string Code, string Name, string Phone, string Address,
    string DocumentNumber, bool IsActive, bool IsWalkIn)
{
    public string DisplayText => $"{Code} — {Name} — {Phone}";
    public string StatusText => IsWalkIn ? "Khách hệ thống — không gán CCCD" : IsActive ? "Đang hoạt động" : "Ngừng hoạt động";
}
