using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;
using Xunit;

namespace Kazuko.Modules.Production.Tests;

public sealed class WorkerPickerTests
{
    [Fact]
    public void Label_checkbox_and_keyboard_update_multiple_workers_and_summary()=>Sta(()=>
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        foreach(var theme in new[]{"Light","Dark"})foreach(var scale in new[]{0.9,1.0,1.25,1.5})
        {
        app.Resources.Clear();app.Resources.MergedDictionaries.Clear();
        foreach(var name in new[]{theme,"Typography","Controls"})app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {Source=new Uri($"pack://application:,,,/Kazuko.Shared.UI;component/Themes/{name}.xaml")});
        foreach(var key in new[]{"FontSize.Caption","FontSize.Body","FontSize.Subtitle","FontSize.Heading"})
            app.Resources[key]=(double)app.FindResource(key)*scale;
        var vm=new PickerModel();var window=new ProductionOrderDialog{DataContext=vm,Left=-20000,Top=0};
        try
        {
            window.Show();Pump();
            var picker=Assert.IsType<ComboBox>(window.FindName("WorkerPicker"));
            picker.IsDropDownOpen=true;Pump();
            var first=Assert.IsType<ComboBoxItem>(picker.ItemContainerGenerator.ContainerFromIndex(0));
            var check=Descendants(first).OfType<CheckBox>().Single();check.ApplyTemplate();Pump();
            var label=Descendants(check).OfType<TextBlock>().First();
            Click(label);
            Assert.True(vm.SelectableEmployees[0].IsSelected);Assert.True(check.IsChecked);
            Assert.True(picker.IsDropDownOpen);
            AssertSummary(picker,"Lê Thị Hương");
            var second=Assert.IsType<ComboBoxItem>(picker.ItemContainerGenerator.ContainerFromIndex(1));
            var secondCheck=Descendants(second).OfType<CheckBox>().Single();
            Click(secondCheck);
            Assert.True(vm.SelectableEmployees[1].IsSelected);
            AssertSummary(picker,"Lê Thị Hương, Nguyễn Văn An");
            Click(label);Assert.False(vm.SelectableEmployees[0].IsSelected);
            AssertSummary(picker,"Nguyễn Văn An");
            picker.SelectedIndex=0;Pump(); // Arrow navigation/highlighting must not overwrite the summary.
            AssertSummary(picker,"Nguyễn Văn An");
            picker.IsDropDownOpen=true; // Off-screen native popups may lose capture while pumping UI messages.
            PressKey(picker,Key.Space);Assert.True(vm.SelectableEmployees[0].IsSelected);
            AssertSummary(picker,"Lê Thị Hương, Nguyễn Văn An");
            PressKey(picker,Key.Enter);Assert.False(vm.SelectableEmployees[0].IsSelected);
            AssertSummary(picker,"Nguyễn Văn An");
            PressKey(picker,Key.Escape);Assert.False(picker.IsDropDownOpen);
            AssertSummary(picker,"Nguyễn Văn An");
            vm.SelectableEmployees[1].IsSelected=false;
            AssertSummary(picker,"");
            vm.SelectableEmployees[2].IsSelected=true; // Existing saved order includes an inactive worker.
            AssertSummary(picker,"Thợ đã nghỉ (đã nghỉ)");
            picker.IsDropDownOpen=true;Pump();
            Assert.True(Descendants((ComboBoxItem)picker.ItemContainerGenerator.ContainerFromIndex(2)).OfType<CheckBox>().Single().IsChecked);
            vm.IsDraft=false;Pump();Assert.False(picker.IsEnabled);
        }
        finally{window.Close();}
        }
        app.Shutdown();
    });

    private static void AssertSummary(ComboBox picker,string expected)
    {
        Pump();var text=Descendants(picker).OfType<TextBlock>().Single(x=>x.Name=="WorkerSummary");
        Assert.Equal(expected,text.Text);Assert.DoesNotContain(nameof(SelectableEmployeeViewModel),text.Text);
    }
    private static void Click(UIElement source)
    {
        source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
        source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent});
        Pump();
    }
    private static void PressKey(UIElement source,Key key)
    {
        source.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(source),Environment.TickCount,key){RoutedEvent=UIElement.PreviewKeyDownEvent});Pump();
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    private static void Sta(Action action)
    {
        Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}finally{Dispatcher.CurrentDispatcher.InvokeShutdown();}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(error is not null)ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed class PickerModel:ObservableObject
    {
        private bool _isDraft=true;
        public bool IsInHouse=>true;
        public bool IsOutsourced=>false;
        public bool IsDraft{get=>_isDraft;set=>SetProperty(ref _isDraft,value);}
        public ObservableCollection<SelectableEmployeeViewModel> SelectableEmployees{get;}=
            [new(new ProductionEmployeeOption(1,"NV1","Lê Thị Hương")),new(new ProductionEmployeeOption(2,"NV2","Nguyễn Văn An")),
             new(new ProductionEmployeeOption(3,"NV3","Thợ đã nghỉ")){IsInactive=true}];
        public string SelectedWorkerDisplayText=>string.Join(", ",SelectableEmployees.Where(x=>x.IsSelected).Select(x=>x.IsInactive?$"{x.FullName} (đã nghỉ)":x.FullName));
        public PickerModel(){foreach(var item in SelectableEmployees)item.PropertyChanged+=(_,_)=>OnPropertyChanged(nameof(SelectedWorkerDisplayText));}
    }
}
