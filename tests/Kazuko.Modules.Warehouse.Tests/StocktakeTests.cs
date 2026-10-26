using Kazuko.Core.Authentication;
using Kazuko.Core.UserManagement;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.UserManagement;
using Kazuko.Modules.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class StocktakeTests
{
    [Theory]
    [InlineData("MATERIAL",-1,false)]
    [InlineData("MATERIAL",1.125,true)]
    [InlineData("MATERIAL",1.12345,false)]
    [InlineData("SERIAL",0,true)]
    [InlineData("SERIAL",1,true)]
    [InlineData("SERIAL",2,false)]
    [InlineData("UNKNOWN",0.5,false)]
    public void Counts_reject_negative_excess_precision_and_fractional_serials(string kind,double value,bool valid)
    {
        if(valid)StocktakeValidation.ValidateQuantity(kind,(decimal)value);
        else Assert.Throws<InvalidOperationException>(()=>StocktakeValidation.ValidateQuantity(kind,(decimal)value));
    }

    [Theory]
    [InlineData("ListAsync",true)] [InlineData("GetAsync",true)] [InlineData("GetLocationsAsync",true)]
    [InlineData("CreateAsync",false)] [InlineData("SaveAsync",false)] [InlineData("ScanAsync",false)]
    [InlineData("ReviewAsync",false)] [InlineData("CloseAsync",false)]
    public void Read_permission_cannot_mutate_counts(string method,bool allowed)
    {
        var rights=new PermissionSnapshot(false,[new GroupPermissionRow(1,"INV_STOCKTAKE",true,false,false,false,false)]);
        Assert.Equal(allowed,BackendAccess.Allows(nameof(IStocktakeService),method,[],rights));
        Assert.False(BackendAccess.Allows(nameof(IStocktakeService),method,[],PermissionSnapshot.Denied));
    }

    [Fact]
    public async Task Save_commits_pending_editor_input_and_includes_hidden_rows()
    {
        var service=new FakeService();var vm=await Create(service);
        vm.SelectedLine=vm.Lines[0];vm.QuantityText="1.250,5";vm.LineNotes="Đếm thực tế";
        vm.SearchText="A";
        vm.SaveCommand.Execute(null);
        Assert.Equal("",vm.Error);
        Assert.NotNull(service.Saved);
        Assert.Equal(2,service.Saved.Lines.Count);
        Assert.Equal(1250.5m,service.Saved.Lines[0].Quantity);
        Assert.True(service.Saved.Lines[0].ConfirmRecount);
        Assert.Equal("Đếm thực tế",service.Saved.Lines[0].Notes);
    }

    [Fact]
    public async Task Invalid_pending_value_blocks_save_without_losing_editor_text()
    {
        var service=new FakeService();var vm=await Create(service);
        vm.SelectedLine=vm.Lines[0];vm.QuantityText="-2";
        vm.SaveCommand.Execute(null);
        Assert.Null(service.Saved);Assert.NotEmpty(vm.Error);Assert.Equal("-2",vm.QuantityText);
        Assert.Equal(2,vm.Lines.Count);
    }

    [Fact]
    public void Recount_zero_requires_explicit_confirmation_and_does_not_add_serial_quantity()
    {
        var line=new StocktakeLineRow(new(1,"F:1","SERIAL",1,"SN1","Nhẫn","món",1,0,"","stamp","IN_STOCK",true));
        Assert.True(line.Model.NeedsRecount);
        line.Change(0,"");Assert.True(line.Confirmed);Assert.False(line.Model.NeedsRecount);
        line.Change(1,"");line.Change(1,"");Assert.Equal(1,line.Model.CountedQuantity);
    }

    private static async Task<StocktakeTabViewModel> Create(FakeService service)
    {
        var session=new CurrentSessionAccessor();session.Set(new(1,"test","Test","ADMIN",1,"CN","Chi nhánh"));
        var vm=new StocktakeTabViewModel(service,session,confirm:(_,_)=>true);
        await vm.InitializeAsync();vm.SelectedCount=vm.Counts[0];return vm;
    }
    private sealed class FakeService : IStocktakeService
    {
        public StocktakeSaveRequest? Saved;
        private StocktakeDetail Detail=new(1,"KK-1",DateTime.Today,1,"Kho NVL","MATERIALS","DRAFT",1,0,"","Test",
            [new(1,"M:1","MATERIAL",1,"A","Vàng","CHI",10,null,"","1","",true),new(2,"M:2","MATERIAL",2,"B","Đá","VIEN",3,3,"","2","")]);
        public Task<IReadOnlyList<WarehouseLocationOption>> GetLocationsAsync(AuthenticatedSession s,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<WarehouseLocationOption>>([new(1,1,"K","Kho","WAREHOUSE")]);
        public Task<IReadOnlyList<StocktakeSummary>> ListAsync(AuthenticatedSession s,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<StocktakeSummary>>([new(1,"KK-1",DateTime.Today,"Kho","MATERIALS","DRAFT",Detail.Revision,"Test")]);
        public Task<StocktakeDetail> GetAsync(long id,AuthenticatedSession s,CancellationToken ct=default)=>Task.FromResult(Detail);
        public Task<StocktakeDetail> SaveAsync(StocktakeSaveRequest request,AuthenticatedSession s,CancellationToken ct=default)
        {
            Saved=request;Detail=Detail with{Revision=Detail.Revision+1,Lines=Detail.Lines.Select(x=>
            {var input=request.Lines.Single(y=>y.LineId==x.Id);return x with{CountedQuantity=input.Quantity,Notes=input.Notes,NeedsRecount=x.NeedsRecount&&!input.ConfirmRecount};}).ToArray()};
            return Task.FromResult(Detail);
        }
        public Task<StocktakeDetail> CreateAsync(StocktakeCreateRequest r,AuthenticatedSession s,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<StocktakeDetail> ScanAsync(long id,int revision,string scan,AuthenticatedSession s,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<StocktakeDetail> ReviewAsync(long id,int revision,AuthenticatedSession s,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<StocktakeDetail> CloseAsync(long id,int revision,AuthenticatedSession s,CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
