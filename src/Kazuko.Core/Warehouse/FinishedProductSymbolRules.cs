namespace Kazuko.Core.Warehouse;

public static class FinishedProductSymbolRules
{
    public const int MaxLength = 40;

    public static string Normalize(string? code)
    {
        var trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Ký hiệu không được để trống.");
        if (trimmed.Length > MaxLength)
            throw new InvalidOperationException($"Ký hiệu tối đa {MaxLength} ký tự.");
        return trimmed;
    }
}
