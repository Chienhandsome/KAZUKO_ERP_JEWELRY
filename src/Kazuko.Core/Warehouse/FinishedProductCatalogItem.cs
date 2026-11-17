namespace Kazuko.Core.Warehouse;

public sealed record FinishedProductCatalogItem(long Id, string Code, string Name, bool IsActive)
{
    public string Phone => string.Empty;
    public string Address => string.Empty;
    public string Email => string.Empty;
}