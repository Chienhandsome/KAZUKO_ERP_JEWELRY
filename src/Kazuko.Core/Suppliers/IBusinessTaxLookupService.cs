namespace Kazuko.Core.Suppliers;

public sealed record BusinessTaxInfo(string TaxCode, string Name, string Address, string? LegalRepresentative = null);

public interface IBusinessTaxLookupService
{
    Task<BusinessTaxInfo?> LookupAsync(string taxCode, CancellationToken cancellationToken = default);
}

public static class BusinessTaxLookupInput
{
    public const string InvalidFormatMessage =
        "Vui lòng nhập MST gốc 10 chữ số.";

    public static bool TryNormalize(string? input, out string taxCode)
    {
        taxCode = RemoveWhitespace(input);
        return taxCode.Length == 10 && taxCode.All(char.IsAsciiDigit);
    }

    public static bool TryGetLookupCodes(string? input, out string fullCode, out string rootCode)
    {
        fullCode = RemoveWhitespace(input);
        rootCode = fullCode.Length == 14 && fullCode[10] == '-' && fullCode[11..].All(char.IsAsciiDigit)
            ? fullCode[..10]
            : fullCode;
        return rootCode.Length == 10 && rootCode.All(char.IsAsciiDigit);
    }

    private static string RemoveWhitespace(string? input) =>
        string.Concat((input ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));
}
