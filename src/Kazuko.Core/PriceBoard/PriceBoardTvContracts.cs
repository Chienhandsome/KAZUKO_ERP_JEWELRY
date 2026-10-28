using System.Text.RegularExpressions;
using System.Globalization;

namespace Kazuko.Core.PriceBoard;

public sealed record TvGoldOption(long Id, bool Visible, int Order);
public sealed record PriceBoardTvSettings(
    bool Enabled = false, bool AllowLan = false, int Port = 8878, string ShopName = "", string Ticker = "",
    string TitleColor = "#FFFF00", string BackgroundColor = "#702539", string BorderColor = "#FFFFFF", string PriceColor = "#FFFF00",
    string Alignment = "center", string UnitCode = "CHI", int RowsPerPage = 8,
    IReadOnlyList<TvGoldOption>? GoldOptions = null, string MonitorDevice = "")
{
    public void Validate()
    {
        if (Port is < 1024 or > 65535) throw new InvalidOperationException("Cổng TV phải từ 1024 đến 65535.");
        if (ShopName is null || Ticker is null || ShopName.Length > 150 || Ticker.Length > 1000) throw new InvalidOperationException("Tên tiệm tối đa 150 ký tự, chữ chạy tối đa 1000 ký tự.");
        foreach (var color in new[] { TitleColor, BackgroundColor, BorderColor, PriceColor })
            if (!Regex.IsMatch(color ?? "", "^#[0-9A-Fa-f]{6}$")) throw new InvalidOperationException("Màu cần có dạng #RRGGBB.");
        if (Alignment is not ("left" or "center" or "right")) throw new InvalidOperationException("Căn lề không hợp lệ.");
        if (UnitCode is not ("CHI" or "LUONG")) throw new InvalidOperationException("Đơn vị TV chỉ hỗ trợ chỉ hoặc lượng.");
        if (RowsPerPage is < 3 or > 15) throw new InvalidOperationException("Mỗi trang hiển thị từ 3 đến 15 loại vàng.");
        if (GoldOptions is not null && (GoldOptions.Any(x => x.Id <= 0) || GoldOptions.Select(x => x.Id).Distinct().Count() != GoldOptions.Count))
            throw new InvalidOperationException("Danh sách loại vàng bị trùng hoặc không hợp lệ.");
    }
}

public sealed record TvPriceRow(string Name, string Buy, string Sell);
public sealed record PriceBoardTvSnapshot(bool Active, string ShopName, string Ticker, string TitleColor, string BackgroundColor,
    string BorderColor, string PriceColor, string Alignment, string UnitLabel, int RowsPerPage,
    string UpdatedLabel, string Status, bool Stale, IReadOnlyList<TvPriceRow> Rows);

public interface IPriceBoardTvSettingsStore
{
    PriceBoardTvSettings Load(long companyId, long branchId);
    void Save(long companyId, long branchId, PriceBoardTvSettings settings);
}
public interface IPriceBoardTvServer : IDisposable
{
    string LocalUrl { get; }
    IReadOnlyList<string> LanUrls { get; }
}
public interface IPriceBoardTvServerFactory
{
    IPriceBoardTvServer Start(int port, bool allowLan, Func<PriceBoardTvSnapshot> readSnapshot);
}
public interface IPriceBoardTvController
{
    void ShowSettings();
    void Stop();
}

public static class PriceBoardTvSnapshotBuilder
{
    public static PriceBoardTvSnapshot Build(PriceBoardTvSettings settings,IReadOnlyList<GoldPriceRecord> prices,long companyId,
        bool active=true,bool stale=false,string status="")
    {
        settings.Validate();var options=(settings.GoldOptions??[]).ToDictionary(x=>x.Id);
        var rows=prices.Where(x=>x.CompanyId==companyId&&x.IsActive&&x.CurrencyCode=="VND"&&(!options.TryGetValue(x.Id,out var option)||option.Visible))
            .OrderBy(x=>options.TryGetValue(x.Id,out var option)?option.Order:x.DisplayOrder).ThenBy(x=>x.Id).ToArray();
        var unit=settings.UnitCode=="LUONG"?"lượng":"chỉ";
        return new(active,settings.ShopName,settings.Ticker,settings.TitleColor,settings.BackgroundColor,settings.BorderColor,settings.PriceColor,
            settings.Alignment,"VND / "+unit,settings.RowsPerPage,rows.Length==0?"":rows.Max(x=>x.UpdatedAt).ToString("dd/MM/yyyy HH:mm:ss",CultureInfo.InvariantCulture),status,stale,
            active?rows.Select(x=>new TvPriceRow(x.Name,x.DisplayBuyPrice(settings.UnitCode).ToString("N0",CultureInfo.GetCultureInfo("vi-VN")),
                x.DisplaySellPrice(settings.UnitCode).ToString("N0",CultureInfo.GetCultureInfo("vi-VN")))).ToArray():[]);
    }
}
