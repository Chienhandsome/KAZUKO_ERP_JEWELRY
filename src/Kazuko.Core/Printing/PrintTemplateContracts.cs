using Kazuko.Core.Authentication;

namespace Kazuko.Core.Printing;

/// <summary>Stable codes for printable FastReport templates stored in PRINT_TEMPLATES.</summary>
public static class PrintTemplateCodes
{
    /// <summary>Bill A5 ngang (landscape).</summary>
    public const string SalesReceiptA5 = "SALES_RECEIPT_A5";

    /// <summary>Bill A5 dọc (portrait).</summary>
    public const string SalesReceiptA5Portrait = "SALES_RECEIPT_A5_PORTRAIT";
}

/// <summary>Orientation for sales receipt print (local preference).</summary>
public enum SalesReceiptOrientation
{
    Landscape = 0,
    Portrait = 1
}

public static class SalesReceiptOrientationCodes
{
    public static string ToTemplateCode(SalesReceiptOrientation orientation) =>
        orientation == SalesReceiptOrientation.Portrait
            ? PrintTemplateCodes.SalesReceiptA5Portrait
            : PrintTemplateCodes.SalesReceiptA5;

    public static string ToDisplayName(SalesReceiptOrientation orientation) =>
        orientation == SalesReceiptOrientation.Portrait ? "A5 dọc" : "A5 ngang";
}

public sealed record PrintTemplateDto(
    long Id,
    string Code,
    string Name,
    byte[] Content,
    string ContentHash,
    DateTime UpdatedAt,
    string? Notes);

public interface IPrintTemplateStore
{
    Task<PrintTemplateDto> GetByCodeAsync(
        string code,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<PrintTemplateDto> SaveAsync(
        string code,
        string name,
        byte[] content,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
