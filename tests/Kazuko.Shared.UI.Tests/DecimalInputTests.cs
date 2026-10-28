using System.ComponentModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Kazuko.Core.Warehouse;
using Kazuko.Shared.UI.Behaviors;
using Kazuko.Shared.UI.Converters;
using Xunit;

namespace Kazuko.Shared.UI.Tests;

public sealed class DecimalInputTests
{
    [Theory]
    [InlineData("en-US")][InlineData("vi-VN")][InlineData("de-DE")][InlineData("fr-FR")]
    public void Plain_weights_and_dimensions_preserve_the_decimal_on_every_culture(string name)
    {
        var culture=CultureInfo.GetCultureInfo(name);var converter=new PlainDecimalConverter();
        foreach(var pair in new Dictionary<string,decimal>{{"0.5",.5m},{"0,5",.5m},{"0.023",.023m},{"0,023",.023m},{"1.250",1.25m},{"1,250",1.25m},{"0.0001",.0001m}})
        {
            Assert.True(WeightDecimalInput.TryParse(pair.Key,out var weight));Assert.Equal(pair.Value,weight);
            Assert.Equal(pair.Value,converter.ConvertBack(pair.Key,typeof(decimal),null,culture));
            Assert.Equal((double)pair.Value,converter.ConvertBack(pair.Key,typeof(double),null,culture));
        }
        foreach(var invalid in new[]{"1.250,5","1,250.5","0..5","abc0.5","0.00001"})
        {
            Assert.False(WeightDecimalInput.TryParse(invalid,out _));
            Assert.Same(DependencyProperty.UnsetValue,converter.ConvertBack(invalid,typeof(decimal),null,culture));
            Assert.Equal(0m,converter.ConvertBack(invalid,typeof(decimal),"PositiveQuantity",culture));
        }
    }
    [Fact]
    public void Grouped_quantities_keep_existing_vietnamese_thousands_without_misreading_zero_decimals()
    {
        foreach(var pair in new Dictionary<string,decimal>{{"0.5",.5m},{"0.023",.023m},{"0,023",.023m},{"1.250",1250m},{"1.250,5",1250.5m},{"1.000.000",1000000m},{"1.25",1.25m}})
        {Assert.True(ViNumberFormat.TryParseQuantity(pair.Key,out var number));Assert.Equal(pair.Value,number);}
        foreach(var invalid in new[]{"1.2.3","1,250.5","abc123","12 34","0.00001"})Assert.False(ViNumberFormat.TryParseQuantity(invalid,out _));
        Assert.True(ViNumberFormat.TryFormatInput("0.023",true,5,out var formatted,out _));Assert.Equal("0,023",formatted);
        Assert.True(ViNumberFormat.TryFormatInput("1000.000",true,8,out var largeFormatted,out _));
        Assert.True(ViNumberFormat.TryParseQuantity(largeFormatted,out var large));Assert.Equal(1000m,large);
        Assert.False(ViNumberFormat.TryFormatInput("abc0.023",true,8,out _,out _));
    }
    [Theory]
    [InlineData("en-US")][InlineData("vi-VN")][InlineData("de-DE")][InlineData("fr-FR")]
    public void Real_textbox_binding_and_live_paste_do_not_multiply_weight(string culture)=>Sta(()=>
    {
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
        var model=new AmountModel();var box=new TextBox();
        box.SetBinding(TextBox.TextProperty,new Binding(nameof(model.Amount)){Source=model,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,Converter=new PlainDecimalConverter(),ConverterParameter="PositiveQuantity"});
        foreach(var input in new[]{"0.5","0,023","0.023"})
        {box.Text=input;box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Drain();Assert.True(WeightDecimalInput.TryParse(input,out var expected));Assert.Equal(expected,model.Amount);}
        box.Text="1.2.3";box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Assert.Equal(0m,model.Amount);
        var grouped=new TextBox();grouped.SetBinding(TextBox.TextProperty,new Binding(nameof(model.Amount)){Source=model,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,Converter=new VietnameseQuantityConverter()});
        NumericThousandSeparatorBehavior.SetAllowDecimal(grouped,true);NumericThousandSeparatorBehavior.SetEnableLiveThousandSeparator(grouped,true);
        grouped.SelectAll();grouped.RaiseEvent(new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText,"0.023"),false,DataFormats.UnicodeText));Drain();
        grouped.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Assert.Equal(.023m,model.Amount);Assert.Equal("0,023",grouped.Text);
    });
    private static void Drain()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Sta(Action action)
    {
        Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed class AmountModel:INotifyPropertyChanged
    {
        private decimal _amount;public decimal Amount{get=>_amount;set{_amount=value;PropertyChanged?.Invoke(this,new(nameof(Amount)));}}
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
