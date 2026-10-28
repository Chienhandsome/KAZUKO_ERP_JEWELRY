using System.Globalization;
using Xunit;

namespace Kazuko.Modules.Debt.Tests;

public sealed class GoldRepaymentInputTests
{
    [Theory]
    [InlineData("en-US")][InlineData("vi-VN")][InlineData("de-DE")][InlineData("fr-FR")]
    public void Repayment_accepts_dot_and_comma_without_increasing_gold(string name)
    {
        var old=CultureInfo.CurrentCulture;CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(name);
        try
        {
            foreach(var text in new[]{"0.023","0,023"})
            {Assert.True(WorkerGoldRepaymentDialog.TryParseAmount(text,out var amount));Assert.Equal(.023m,amount);}
            foreach(var text in new[]{"1.250,5","1,250.5","0..023","abc23","0.00001"})Assert.False(WorkerGoldRepaymentDialog.TryParseAmount(text,out _));
        }
        finally{CultureInfo.CurrentCulture=old;}
    }
}
