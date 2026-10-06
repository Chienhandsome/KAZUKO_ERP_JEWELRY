namespace Kazuko.Modules.Reports.Shared;

/// <summary>Định nghĩa cột báo cáo (khóa ổn định, không dùng header tiếng Việt làm key).</summary>
public sealed record ReportColumnDefinition(
    string Key,
    string Header,
    float WidthWeight,
    bool DefaultVisible = true);
