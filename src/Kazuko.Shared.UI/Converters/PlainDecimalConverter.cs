using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Kazuko.Core.Warehouse;

namespace Kazuko.Shared.UI.Converters;

/// <summary>Weights/measurements: one dot OR comma is decimal, no thousands grouping, independent of OS culture.</summary>
public sealed class PlainDecimalConverter : IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)
        =>value is IConvertible?System.Convert.ToDecimal(value,CultureInfo.InvariantCulture).ToString("0.####",CultureInfo.GetCultureInfo("vi-VN")):string.Empty;
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)
    {
        var text=value as string??"";
        var type=Nullable.GetUnderlyingType(targetType)??targetType;
        if(string.IsNullOrWhiteSpace(text))return Nullable.GetUnderlyingType(targetType) is not null?null!:Cast(0,type);
        if(!WeightDecimalInput.TryParse(text,out var number))
            return parameter as string=="PositiveQuantity"?Cast(0,type):DependencyProperty.UnsetValue;
        if((type==typeof(int)||type==typeof(long))&&decimal.Truncate(number)!=number)return DependencyProperty.UnsetValue;
        try{return Cast(number,type);}catch(OverflowException){return DependencyProperty.UnsetValue;}
    }
    private static object Cast(decimal number,Type type)
    {
        if(type==typeof(double))return (double)number;
        if(type==typeof(float))return (float)number;
        if(type==typeof(int))return checked((int)number);
        if(type==typeof(long))return checked((long)number);
        return number;
    }
}
