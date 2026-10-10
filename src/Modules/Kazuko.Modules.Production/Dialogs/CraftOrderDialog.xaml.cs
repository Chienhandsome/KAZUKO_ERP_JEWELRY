using System.Windows;

namespace Kazuko.Modules.Production.Dialogs;

public partial class CraftOrderDialog : Window
{
    public CraftOrderDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CraftOrderDialogViewModel oldVm)
            oldVm.RequestClose -= OnRequestClose;
        if (e.NewValue is CraftOrderDialogViewModel newVm)
            newVm.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(bool accepted) => DialogResult = accepted;
}
