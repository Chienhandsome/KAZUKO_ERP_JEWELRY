using System.Collections.ObjectModel;

namespace Kazuko.Modules.Reports;

public sealed class ReportTreeNode
{
    private ReportTreeNode(string title, string? reportKey, bool isExpanded)
    {
        Title = title;
        ReportKey = reportKey;
        IsExpanded = isExpanded;
    }

    public string Title { get; }
    public string? ReportKey { get; }
    public bool IsExpanded { get; }
    public bool IsReport => !string.IsNullOrWhiteSpace(ReportKey);
    public ObservableCollection<ReportTreeNode> Children { get; } = [];

    public static ReportTreeNode Group(string title, params ReportTreeNode[] children)
    {
        var node = new ReportTreeNode(title, reportKey: null, isExpanded: true);
        foreach (var child in children)
            node.Children.Add(child);
        return node;
    }

    public static ReportTreeNode Report(string title, string reportKey) =>
        new(title, reportKey, isExpanded: false);
}
