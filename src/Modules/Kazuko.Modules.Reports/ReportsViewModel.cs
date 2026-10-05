using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Reports;

public sealed class ReportsViewModel : ObservableObject
{
    public const string JewelryOnHandKey = "reports.warehouse.jewelry_on_hand";

    private ReportTreeNode? _selectedNode;
    private string _placeholderTitle = "Báo cáo";
    private string _placeholderMessage = "Chọn một báo cáo trong danh sách bên trái.";
    private bool _showPlaceholder = true;
    private bool _showJewelryOnHand;
    private bool _isCatalogVisible = true;

    public ReportsViewModel(JewelryOnHandReportViewModel jewelryOnHand)
    {
        JewelryOnHand = jewelryOnHand;
        Menu = ReportCatalog.CreateMenu();
        ToggleCatalogCommand = new RelayCommand(ToggleCatalog);
    }

    public ObservableCollection<ReportTreeNode> Menu { get; }
    public JewelryOnHandReportViewModel JewelryOnHand { get; }
    public ICommand ToggleCatalogCommand { get; }

    public ReportTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
                return;

            UpdateContent();
        }
    }

    public bool IsCatalogVisible
    {
        get => _isCatalogVisible;
        private set
        {
            if (!SetProperty(ref _isCatalogVisible, value))
                return;

            OnPropertyChanged(nameof(CatalogToggleLabel));
        }
    }

    public string CatalogToggleLabel => IsCatalogVisible ? "Ẩn danh mục" : "Hiện danh mục";

    public string PlaceholderTitle
    {
        get => _placeholderTitle;
        private set => SetProperty(ref _placeholderTitle, value);
    }

    public string PlaceholderMessage
    {
        get => _placeholderMessage;
        private set => SetProperty(ref _placeholderMessage, value);
    }

    public bool ShowPlaceholder
    {
        get => _showPlaceholder;
        private set => SetProperty(ref _showPlaceholder, value);
    }

    public bool ShowJewelryOnHand
    {
        get => _showJewelryOnHand;
        private set => SetProperty(ref _showJewelryOnHand, value);
    }

    private void ToggleCatalog() => IsCatalogVisible = !IsCatalogVisible;

    private void UpdateContent()
    {
        if (SelectedNode is null)
        {
            PlaceholderTitle = "Báo cáo";
            PlaceholderMessage = "Chọn một báo cáo trong danh sách bên trái.";
            ShowJewelryOnHand = false;
            ShowPlaceholder = true;
            return;
        }

        if (SelectedNode.IsReport
            && string.Equals(SelectedNode.ReportKey, JewelryOnHandKey, StringComparison.Ordinal))
        {
            ShowPlaceholder = false;
            ShowJewelryOnHand = true;
            _ = JewelryOnHand.EnsureLoadedAsync();
            return;
        }

        ShowJewelryOnHand = false;
        ShowPlaceholder = true;
        PlaceholderTitle = SelectedNode.Title;
        PlaceholderMessage = SelectedNode.IsReport
            ? "Báo cáo này đang được xây dựng. Dữ liệu, bộ lọc và xuất file sẽ được bổ sung trong các bản cập nhật tiếp theo."
            : "Mở mục con và chọn báo cáo cụ thể.";
    }
}
