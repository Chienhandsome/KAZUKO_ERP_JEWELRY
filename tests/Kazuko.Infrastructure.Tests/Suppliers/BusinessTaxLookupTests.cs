using System.Net;
using Kazuko.Infrastructure.Suppliers;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Suppliers;

public sealed class BusinessTaxLookupTests
{
    [Fact]
    public async Task Root_tax_code_maps_business_fields_without_api_credentials()
    {
        var handler = new StubHandler("""{"code":"00","data":{"id":"0316227737","name":"FINSMART","address":"Địa chỉ công ty"}}""");
        using var client = new HttpClient(handler);
        var result = await new VietQrBusinessTaxLookupService(client).LookupAsync(" 0316 227737 ");
        Assert.NotNull(result);
        Assert.Equal("0316227737", result.TaxCode);
        Assert.Equal("FINSMART", result.Name);
        Assert.Equal("Địa chỉ công ty", result.Address);
        Assert.Equal("https://api.vietqr.io/v2/business/0316227737", handler.RequestUri);
    }

    [Theory]
    [InlineData("0316227737-001")]
    [InlineData("0316227737001")]
    [InlineData("0303580913-003")]
    [InlineData("0303580913003")]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("0316227737?x=1")]
    public async Task Branch_and_invalid_codes_do_not_call_api_or_fallback_to_parent(string input)
    {
        var handler = new StubHandler("{}");
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VietQrBusinessTaxLookupService(client).LookupAsync(input));
        Assert.Null(handler.RequestUri);
    }

    [Theory]
    [InlineData("{\"code\":\"51\",\"data\":null}")]
    [InlineData("{\"code\":\"00\",\"data\":null}")]
    public async Task Missing_business_returns_no_result(string json)
    {
        using var client = new HttpClient(new StubHandler(json));
        Assert.Null(await new VietQrBusinessTaxLookupService(client).LookupAsync("0316227737"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"code\":\"00\",\"data\":{\"id\":\"0303580913\",\"name\":\"Other\"}}")]
    [InlineData("{\"code\":\"00\",\"data\":{\"id\":\"0316227737\"}}")]
    public async Task Invalid_or_mismatched_data_is_rejected(string json)
    {
        using var client = new HttpClient(new StubHandler(json));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VietQrBusinessTaxLookupService(client).LookupAsync("0316227737"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Api_errors_are_reported(HttpStatusCode status)
    {
        using var client = new HttpClient(new StubHandler("{}", status));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VietQrBusinessTaxLookupService(client).LookupAsync("0316227737"));
    }

    [Fact]
    public async Task Cancellation_is_preserved()
    {
        using var client = new HttpClient(new StubHandler("{}"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VietQrBusinessTaxLookupService(client).LookupAsync("0316227737", cancellation.Token));
    }

    private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.False(request.Headers.Contains("api-key"));
            Assert.False(request.Headers.Contains("client-id"));
            RequestUri = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }
}
