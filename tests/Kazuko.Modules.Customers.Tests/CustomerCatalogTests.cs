using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kazuko.Core.UserManagement;
using Kazuko.Shared.UI.Controls;
using Xunit;

namespace Kazuko.Modules.Customers.Tests;

public sealed class CustomerCatalogTests
{
    [Fact]
    public async Task Typing_filters_the_list_and_clearing_restores_it()
    {
        var repository = new CustomerRepositoryFake
        {
            Customers =
            [
                CustomerRepositoryFake.Sample(),
                CustomerRepositoryFake.Sample(2) with { Name = "Trần Văn An", Phone = "0987654321" }
            ]
        };
        var viewModel = CreateViewModel(repository);
        await viewModel.LoadAsync(1);

        viewModel.Query = " trần ";
        Assert.Equal(2, Assert.Single(viewModel.Rows).Id);
        viewModel.Query = "kh001";
        Assert.Equal(1, Assert.Single(viewModel.Rows).Id);
        viewModel.Query = "0987";
        Assert.Equal(2, Assert.Single(viewModel.Rows).Id);
        viewModel.Query = "không khớp";
        Assert.Empty(viewModel.Rows);
        Assert.Null(viewModel.Selected);
        viewModel.Query = "";
        Assert.Equal(2, viewModel.Rows.Count);
    }

    [Fact]
    public async Task Archive_keeps_row_visible_but_prevents_choosing_it()
    {
        var repository = new CustomerRepositoryFake
        {
            Customers = [CustomerRepositoryFake.Sample(), CustomerRepositoryFake.Sample(2) with { IsWalkIn = true }]
        };
        var viewModel = CreateViewModel(repository, allowWalkIn: false);
        await viewModel.LoadAsync(1);

        Assert.Single(viewModel.Rows);
        Assert.True(viewModel.ChooseCommand.CanExecute(null));
        viewModel.DeleteCommand.Execute(null);

        Assert.True(viewModel.HasChanges);
        Assert.False(viewModel.Selected!.IsActive);
        Assert.False(viewModel.ChooseCommand.CanExecute(null));
        Assert.False(viewModel.DeleteCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("Light", 0.9)]
    [InlineData("Light", 1.0)]
    [InlineData("Light", 1.25)]
    [InlineData("Light", 1.5)]
    [InlineData("Dark", 0.9)]
    [InlineData("Dark", 1.0)]
    [InlineData("Dark", 1.25)]
    [InlineData("Dark", 1.5)]
    public void Catalog_actions_fit_with_themed_text_and_long_customer_details(string theme, double scale)
    {
        CustomerTestContext.Sta(() =>
        {
            var repository = new CustomerRepositoryFake();
            var viewModel = CreateViewModel(repository);
            var dialog = new CatalogDialog { Title = "Danh mục khách hàng", DataContext = viewModel };
            var view = (FrameworkElement)dialog.Content;
            foreach (System.Collections.DictionaryEntry resource in dialog.Resources)
                view.Resources[resource.Key] = resource.Value;
            foreach (var name in new[] { theme, "Typography", "Controls" })
                view.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{name}.xaml", UriKind.Relative) });
            foreach (var key in new[] { "FontSize.Caption", "FontSize.Body", "FontSize.Subtitle" })
                view.Resources[key] = (double)view.FindResource(key) * scale;
            dialog.Content = null;
            view.DataContext = viewModel;
            var host = new Border { Child = view, Background = (Brush)view.FindResource("AppBackgroundBrush") };

            foreach (var hasCustomer in new[] { false, true })
            {
                repository.Customers = hasCustomer
                    ? [CustomerRepositoryFake.Sample() with { Address = string.Join(" ", Enumerable.Repeat("Địa chỉ khách hàng rất dài", 8)) }]
                    : [];
                viewModel.LoadAsync(null).GetAwaiter().GetResult();

                foreach (var size in new[] { new Size(644, 381), new Size(924, 601) })
                {
                    host.Measure(size);
                    host.Arrange(new Rect(size));
                    host.UpdateLayout();
                    var buttons = Descendants(view).OfType<Button>()
                        .Where(button => button.Visibility == Visibility.Visible && button.Content is string).ToArray();
                    Assert.Equal(5, buttons.Length);
                    foreach (var button in buttons)
                    {
                        Assert.True(button.ActualHeight >= 44);
                        var bounds = button.TransformToAncestor(host).TransformBounds(new Rect(button.RenderSize));
                        Assert.InRange(bounds.Bottom, 0, size.Height);
                        Assert.InRange(bounds.Right, 0, size.Width);
                    }
                    foreach (var dpi in new[] { 1.0, 1.5, 2.0 })
                    {
                        var bitmap = new RenderTargetBitmap((int)(size.Width * dpi), (int)(size.Height * dpi),
                            96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
                        bitmap.Render(host);
                    }
                }
            }
            dialog.Close();
        });
    }

    private static CustomerCatalogViewModel CreateViewModel(CustomerRepositoryFake repository, bool allowWalkIn = true) =>
        new(repository, CustomerTestContext.Session(), new Administrator(), new CustomerEditorFake(),
            selectCustomer: true, allowWalkIn: allowWalkIn, confirm: (_, _) => true);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private sealed class Administrator : IAuthorizationService
    {
        public PermissionSnapshot Current { get; } = new(true, []);
        public event EventHandler? Changed { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Clear() { }
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
