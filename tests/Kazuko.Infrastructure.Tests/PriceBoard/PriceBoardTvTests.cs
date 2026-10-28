using System.Net;
using System.Text.Json;
using Kazuko.Core.PriceBoard;
using Kazuko.Infrastructure.PriceBoard;
using Xunit;

namespace Kazuko.Infrastructure.Tests.PriceBoard;

public sealed class PriceBoardTvTests
{
    private static GoldPriceRecord Price(long id,long company=1,bool active=true)=>new(id,company,"G"+id,"Vàng "+id,75,1000000+id,1200000+id,0,0,"VND",(int)id,active,"admin",new DateTime(2026,10,26,10,0,0));

    [Fact]
    public void Snapshot_uses_retail_prices_filters_company_inactive_hidden_and_preserves_order()
    {
        var settings=new PriceBoardTvSettings(UnitCode:"LUONG",GoldOptions:[new(1,false,0),new(3,true,0),new(2,true,1)]);
        var snapshot=PriceBoardTvSnapshotBuilder.Build(settings,[Price(1),Price(2),Price(3),Price(4,2),Price(5,active:false)],1);
        Assert.Equal(new[]{"Vàng 3","Vàng 2"},snapshot.Rows.Select(x=>x.Name));
        Assert.Equal("10.000.030",snapshot.Rows[0].Buy);Assert.Equal("12.000.030",snapshot.Rows[0].Sell);
        Assert.Equal("VND / lượng",snapshot.UnitLabel);
        Assert.Empty(PriceBoardTvSnapshotBuilder.Build(settings,[Price(2)],1,active:false).Rows);
    }
    [Theory]
    [InlineData("red")][InlineData("#12345")][InlineData("#000000;display:none")]
    public void Unsafe_or_invalid_color_is_rejected(string color)=>Assert.Throws<InvalidOperationException>(()=>new PriceBoardTvSettings(TitleColor:color).Validate());

    [Fact]
    public void Settings_persist_separately_for_each_company_and_branch()
    {
        var root=Path.Combine(Path.GetTempPath(),"KazukoTvTests",Guid.NewGuid().ToString("N"));
        try
        {
            var store=new JsonPriceBoardTvSettingsStore(root);var value=new PriceBoardTvSettings(ShopName:"Tiệm A",GoldOptions:[new(1,true,0)]);
            store.Save(1,1,value);Assert.Equal("Tiệm A",store.Load(1,1).ShopName);
            Assert.Equal("",store.Load(1,2).ShopName);Assert.Equal("",store.Load(2,1).ShopName);
            Assert.Empty(Directory.GetFiles(root,"*.tmp"));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]
    public async Task Http_server_is_read_only_token_scoped_no_cache_and_updates_its_snapshot()
    {
        var frame=PriceBoardTvSnapshotBuilder.Build(new(ShopName:"<script>alert(1)</script>"),[Price(1)],1);
        using var server=new PriceBoardTvServer(0,false,()=>frame);
        using var client=new HttpClient(new HttpClientHandler{UseProxy=false});
        var page=await client.GetAsync(server.LocalUrl);Assert.Equal(HttpStatusCode.OK,page.StatusCode);
        Assert.Contains("script-src 'self'",page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(page.Headers.CacheControl!.NoStore);Assert.False(page.Headers.Contains("Access-Control-Allow-Origin"));
        var html=await page.Content.ReadAsStringAsync();Assert.DoesNotContain("<script>alert",html);Assert.Contains("app.js",html);
        Assert.Empty(server.LanUrls);
        var data=await client.GetStringAsync(server.LocalUrl+"data");
        using var parsed=JsonDocument.Parse(data);Assert.Equal("<script>alert(1)</script>",parsed.RootElement.GetProperty("shopName").GetString());
        Assert.False(parsed.RootElement.TryGetProperty("userId",out _));Assert.False(parsed.RootElement.TryGetProperty("password",out _));
        frame=frame with{ShopName="Updated"};Assert.Contains("Updated",await client.GetStringAsync(server.LocalUrl+"data"));
        var root=new Uri(server.LocalUrl).GetLeftPart(UriPartial.Authority);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(root+"/data")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(server.LocalUrl+"unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await client.PostAsync(server.LocalUrl+"data",new StringContent("change"))).StatusCode);
        Assert.Contains("textContent",await client.GetStringAsync(server.LocalUrl+"app.js"));
    }
    [Fact]
    public async Task Lan_listener_serves_same_read_only_board_and_releases_port_on_stop()
    {
        var frame=PriceBoardTvSnapshotBuilder.Build(new(),[Price(1)],1);
        var server=new PriceBoardTvServer(0,true,()=>frame);var url=server.LocalUrl;var port=new Uri(url).Port;
        using(var client=new HttpClient(new HttpClientHandler{UseProxy=false}))
        {
            foreach(var address in server.LanUrls)Assert.Contains("rows",await client.GetStringAsync(address+"data"));
        }
        server.Dispose();using var replacement=new PriceBoardTvServer(port,false,()=>frame);
        Assert.NotEqual(url,replacement.LocalUrl); // A new start rotates the capability URL.
    }
}
