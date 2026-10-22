using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Kazuko.Modules.HumanResources;
public partial class PayrollTabView : UserControl
{
 public PayrollTabView() { InitializeComponent(); }
 private void PayrollGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
 {
  var node = e.OriginalSource as DependencyObject;
  while (node is not null && node is not DataGridRow) node = VisualTreeHelper.GetParent(node);
  if (node is DataGridRow && DataContext is PayrollTabViewModel vm && vm.DetailsCommand.CanExecute(null)) vm.DetailsCommand.Execute(null);
 }
}
