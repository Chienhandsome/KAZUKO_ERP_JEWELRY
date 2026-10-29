namespace Kazuko.Core.Suppliers;

public sealed record BusinessTaxInfo(string TaxCode, string Name, string Address);

public interface IBusinessTaxLookupService
{
    Task<BusinessTaxInfo?> LookupAsync(string taxCode, CancellationToken cancellationToken = default);
}
