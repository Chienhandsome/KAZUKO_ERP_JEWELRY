using Kazuko.Core.PriceBoard;
using Xunit;

namespace Kazuko.Infrastructure.Tests.PriceBoard;

public sealed class GoldTypeCatalogValidatorTests
{
    [Fact]
    public void Validate_requires_positive_retail_and_warehouse_prices()
    {
        var errors = GoldTypeCatalogValidator.Validate(
            new GoldTypeUpsertRequest("750", "Vàng 750", 0.75m, 0, 5_000_000, 4_800_000));

        Assert.Contains(errors, e => e.Contains("mua bán lẻ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_requires_issue_price()
    {
        var errors = GoldTypeCatalogValidator.Validate(
            new GoldTypeUpsertRequest("750", "Vàng 750", 0.75m, 5_000_000, 5_200_000, 0));

        Assert.Contains(errors, e => e.Contains("Giá kho", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_accepts_complete_request()
    {
        var errors = GoldTypeCatalogValidator.Validate(
            new GoldTypeUpsertRequest("750", "Vàng 750", 0.75m, 5_000_000, 5_200_000, 4_800_000));

        Assert.Empty(errors);
    }
}
