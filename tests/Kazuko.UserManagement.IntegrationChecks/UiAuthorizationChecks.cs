using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.UserManagement;
using Kazuko.Shared.UI.Behaviors;

public static class UiAuthorizationChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application();
                foreach (var file in new[] { "Light", "Typography", "Controls" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{file}.xaml", UriKind.Relative) });
                var rights = new UiRights();
                PermissionAccess.Initialize(rights);
                var canExecute = true;
                var button = new Button { Content = "Thêm", Command = new RelayCommand(() => { }, () => canExecute) };
                PermissionAccess.SetRule(button, "CAT_CUSTOMERS:Add");
                Check(!button.IsEnabled, "button denied");
                rights.Set(new(false, [new(1, "CAT_CUSTOMERS", true, true, false, false, false)]));
                Pump();
                Check(button.IsEnabled, "button grant refreshed");
                canExecute = false;
                ((RelayCommand)button.Command).NotifyCanExecuteChanged();
                Check(!button.IsEnabled, "existing CanExecute preserved");
                var deniedTab = new TabItem { Header = "Nhân sự" };
                PermissionAccess.SetRule(deniedTab, "CAT_EMPLOYEES");
                Check(deniedTab.Visibility == Visibility.Collapsed, "tab hidden");
                var row = new PermissionMatrixRowViewModel("CAT_CUSTOMERS", "Khách hàng", "Danh mục");
                row.CanAdd = true;
                Check(row.CanView && !row.CanEdit && !row.CanDelete, "matrix independent add");
                row.CanAdd = false; row.CanDelete = true;
                Check(row.CanView && !row.CanEdit && !row.CanAdd, "matrix independent delete");
                var view = new UserManagementView();
                view.Measure(new Size(1280, 800)); view.Arrange(new Rect(0, 0, 1280, 800)); view.UpdateLayout();
                var dialog = new UserEditDialog();
                dialog.ApplyTemplate(); dialog.Close();
                Console.WriteLine("UI_AUTHORIZATION_CHECKS_PASSED");
                application.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("WPF permission smoke check failed", failure);
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Check(bool passed, string name) { if (!passed) throw new Exception(name); Console.WriteLine("PASS " + name); }
    private sealed class UiRights : IAuthorizationService
    {
        public PermissionSnapshot Current { get; private set; } = PermissionSnapshot.Denied;
        public event EventHandler? Changed;
        public void Set(PermissionSnapshot snapshot) { Current = snapshot; Changed?.Invoke(this, EventArgs.Empty); }
        public void Clear() => Set(PermissionSnapshot.Denied);
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
