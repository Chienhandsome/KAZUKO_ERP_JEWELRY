using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kazuko.Core.Suppliers;

namespace Kazuko.Infrastructure.Suppliers;

public sealed class VietQrBusinessTaxLookupService(HttpClient httpClient) : IBusinessTaxLookupService
{
    public async Task<BusinessTaxInfo?> LookupAsync(string taxCode, CancellationToken cancellationToken = default)
    {
        var code = Regex.Replace(taxCode.Trim(), @"\s+", "");
        if (!Regex.IsMatch(code, @"\A[0-9]{10}\z"))
            throw new InvalidOperationException("Tra cứu chỉ hỗ trợ MST gốc 10 số. Với MST chi nhánh, vui lòng nhập thông tin thủ công.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.vietqr.io/v2/business/{code}");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new InvalidOperationException("Dịch vụ tra cứu đang bận. Vui lòng thử lại sau.");
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = json.RootElement;
            if (Read(root, "code") != "00" || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object) return null;
            var id = Read(data, "id");
            var name = Read(data, "name");
            if (id != code || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Kết quả tra cứu không khớp MST hoặc thiếu tên doanh nghiệp. Chưa điền thông tin.");
            return new(id, name, Read(data, "address"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new InvalidOperationException("Tra cứu quá thời gian chờ. Vui lòng thử lại."); }
        catch (HttpRequestException)
        { throw new InvalidOperationException("Không thể kết nối dịch vụ tra cứu MST. Vui lòng kiểm tra mạng và thử lại."); }
        catch (JsonException)
        { throw new InvalidOperationException("Dịch vụ tra cứu trả về dữ liệu không hợp lệ. Vui lòng thử lại."); }
    }
    private static string Read(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
}
