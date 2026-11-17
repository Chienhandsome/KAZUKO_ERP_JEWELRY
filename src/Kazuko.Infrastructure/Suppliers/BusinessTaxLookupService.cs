using System.Net.Http;
using System.Text.Json;
using Kazuko.Core.Suppliers;

namespace Kazuko.Infrastructure.Suppliers;

public sealed class BusinessTaxLookupService(
    HttpClient httpClient, IBusinessTaxLookupService fallback, string? apiKey) : IBusinessTaxLookupService
{
    public async Task<BusinessTaxInfo?> LookupAsync(string taxCode, CancellationToken cancellationToken = default)
    {
        if (!BusinessTaxLookupInput.TryGetLookupCodes(taxCode, out var fullCode, out var rootCode))
            throw new InvalidOperationException("MST không đúng định dạng.");

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var exact = await LookupExactAsync(fullCode, cancellationToken);
            if (exact is not null)
                return exact;
        }

        return await fallback.LookupAsync(rootCode, cancellationToken);
    }

    private async Task<BusinessTaxInfo?> LookupExactAsync(string taxCode, CancellationToken cancellationToken)
    {
        var digits = taxCode.Replace("-", "");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://doanhnghiep.vn/api/v1/companies/{digits}");
            request.Headers.Add("x-api-key", apiKey!.Trim());
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = json.RootElement;
            var returnedCode = Read(root, "mst").Replace("-", "");
            var name = Read(root, "name_vi");
            if (returnedCode != digits || name.Length == 0)
                return null;

            return new BusinessTaxInfo(taxCode, name, Read(root, "address_full"), Read(root, "legal_rep_name"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Read(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
}
