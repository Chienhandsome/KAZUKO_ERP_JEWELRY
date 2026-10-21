using Kazuko.Core.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class FinishedProductSymbolRulesTests
{
    [Fact]
    public void Normalize_trims_the_mark()
    {
        Assert.Equal("G.F", FinishedProductSymbolRules.Normalize("  G.F  "));
    }

    [Fact]
    public void Normalize_rejects_blank_and_too_long()
    {
        Assert.Throws<InvalidOperationException>(() => FinishedProductSymbolRules.Normalize("  "));
        Assert.Throws<InvalidOperationException>(() =>
            FinishedProductSymbolRules.Normalize(new string('A', FinishedProductSymbolRules.MaxLength + 1)));
    }
}
