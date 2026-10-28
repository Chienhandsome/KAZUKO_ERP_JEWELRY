using System.Text.Json;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Infrastructure.PriceBoard;

public sealed class JsonPriceBoardTvSettingsStore(string? root = null) : IPriceBoardTvSettingsStore
{
    private readonly string _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KazukoERP","tv-price-board");
    private string PathFor(long companyId,long branchId)
    {
        if(companyId<=0||branchId<=0)throw new InvalidOperationException("Thiếu công ty/chi nhánh cho bảng giá TV.");
        return Path.Combine(_root,$"{companyId}-{branchId}.json");
    }
    public PriceBoardTvSettings Load(long companyId,long branchId)
    {
        var path=PathFor(companyId,branchId);
        if(!File.Exists(path))return new();
        var value=JsonSerializer.Deserialize<PriceBoardTvSettings>(File.ReadAllText(path))??throw new InvalidOperationException("Không đọc được cấu hình TV.");
        value.Validate();return value;
    }
    public void Save(long companyId,long branchId,PriceBoardTvSettings settings)
    {
        settings.Validate();var path=PathFor(companyId,branchId);Directory.CreateDirectory(_root);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
