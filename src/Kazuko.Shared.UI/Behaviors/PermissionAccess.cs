using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Globalization;
using Kazuko.Core.UserManagement;

namespace Kazuko.Shared.UI.Behaviors;

/// <summary>Explicit XAML rules: FUNCTION:Action, alternatives separated by |. Backend checks remain authoritative.</summary>
public static class PermissionAccess
{
    private static IAuthorizationService? _authorization;
    private static readonly List<WeakReference<DependencyObject>> Controls = [];
    public static readonly DependencyProperty RuleProperty = DependencyProperty.RegisterAttached(
        "Rule", typeof(string), typeof(PermissionAccess), new PropertyMetadata(null, RuleChanged));
    private static readonly DependencyProperty AllowedProperty = DependencyProperty.RegisterAttached(
        "Allowed", typeof(bool), typeof(PermissionAccess), new PropertyMetadata(false));
    private static readonly AccessConverter ButtonConverter = new(false);
    private static readonly AccessConverter TabConverter = new(true);
    public static void SetRule(DependencyObject element, string value) => element.SetValue(RuleProperty, value);
    public static string? GetRule(DependencyObject element) => (string?)element.GetValue(RuleProperty);
    public static void Initialize(IAuthorizationService authorization)
    {
        _authorization = authorization;
        authorization.Changed += (_, _) => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Controls.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in Controls.ToArray()) if (reference.TryGetTarget(out var target)) Update(target);
        });
    }
    private static bool Allowed(DependencyObject element)
    {
        var rule = GetRule(element);
        if (string.IsNullOrEmpty(rule)) return true;
        return rule.Split('|').Any(part =>
        {
            var pair = part.Split(':');
            var action = PermissionAction.View;
            if (pair.Length > 1 && !Enum.TryParse(pair[1], out action)) return false;
            return _authorization?.Current.Allows(pair[0], action) == true;
        });
    }
    private static void RuleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is null)
        {
            Controls.Add(new(d));
            if (d is FrameworkElement element) element.Loaded += (_, _) => Update(element);
        }
        Update(d);
    }
    private static void Update(DependencyObject d)
    {
        d.SetValue(AllowedProperty, Allowed(d));
        if (d is Button button)
        {
            InstallBinding(d, UIElement.IsEnabledProperty, ButtonConverter, true);
            const string denied = "Bạn chưa được cấp quyền cho thao tác này.";
            if (!Allowed(d) && button.ToolTip is null) button.SetCurrentValue(FrameworkElement.ToolTipProperty, denied);
            else if (Allowed(d) && Equals(button.ToolTip, denied)) button.SetCurrentValue(FrameworkElement.ToolTipProperty, null);
            ToolTipService.SetShowOnDisabled(button, true);
        }
        if (d is TabItem tab)
        {
            InstallBinding(d, UIElement.VisibilityProperty, TabConverter, Visibility.Visible);
            if (tab.IsSelected && tab.Visibility != Visibility.Visible && ItemsControl.ItemsControlFromItemContainer(tab) is TabControl parent)
            {
                var index = parent.Items.Cast<object>().Select((item, i) => (item, i))
                    .Where(x => x.item is TabItem candidate && candidate.Visibility == Visibility.Visible)
                    .Select(x => x.i).DefaultIfEmpty(-1).First();
                parent.SetCurrentValue(Selector.SelectedIndexProperty, index);
            }
        }
    }
    private static void InstallBinding(DependencyObject target, DependencyProperty property, AccessConverter converter, object defaultValue)
    {
        var original = BindingOperations.GetBindingBase(target, property);
        if (original is MultiBinding own && ReferenceEquals(own.Converter, converter)) return;
        var combined = new MultiBinding { Mode = BindingMode.OneWay, Converter = converter };
        combined.Bindings.Add(new Binding { Source = target, Path = new PropertyPath(AllowedProperty) });
        combined.Bindings.Add(original ?? new Binding { Source = target.ReadLocalValue(property) is var local && local != DependencyProperty.UnsetValue ? local : defaultValue });
        BindingOperations.SetBinding(target, property, combined);
    }
    private sealed class AccessConverter(bool visibility) : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => visibility
            ? values[0] is true ? values[1] is Visibility original ? original : Visibility.Visible : Visibility.Collapsed
            : values[0] is true && values[1] is true;
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
