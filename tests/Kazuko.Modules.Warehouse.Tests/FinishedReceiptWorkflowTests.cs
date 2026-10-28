using System.Reflection;
using System.Text.Json;
using Kazuko.Core.Company;
using Kazuko.Core.Printing;
using Kazuko.Core.Production;
using Kazuko.Core.Warehouse;
using Kazuko.Core.Sales;
using Kazuko.Modules.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class FinishedReceiptWorkflowTests
{
    [Fact]
    public async Task Old_order_response_cannot_overwrite_new_gold_reconciliation()
    {
        var delayed=new TaskCompletionSource<IReadOnlyList<IssuedGoldByPurityDto>>();
        var vm=Create((method,args)=>method.Name switch
        {
            "GetOrderDetailAsync"=>Task.FromResult<ProductionOrderDetail?>(Detail((long)args[0]!)),
            "GetIssuedGoldByPurityAsync"=>(long)args[0]! == 1?delayed.Task:Task.FromResult<IReadOnlyList<IssuedGoldByPurityDto>>([new("750",20)]),
            "GetIssuedStoneByMaterialAsync"=>Task.FromResult<IReadOnlyList<IssuedStoneByMaterialDto>>([]),
            _=>throw new NotSupportedException()
        });
        vm.SelectedOrder=new(1,"A");Assert.True(vm.IsDetailLoading);
        vm.SelectedOrder=new(2,"B");Assert.False(vm.IsDetailLoading);
        Assert.Equal(20,Assert.Single(vm.GoldReconcileRows).IssuedWeight);
        delayed.SetResult([new("750",1)]);await Task.Delay(25);
        Assert.Equal(2,vm.SelectedOrder!.Id);Assert.Equal(20,Assert.Single(vm.GoldReconcileRows).IssuedWeight);
    }
    [Fact]
    public void Confirmation_prepares_only_and_any_product_change_invalidates_it()
    {
        var writes=0;var vm=Create((m,a)=>m.Name switch
        {
            "GetOrderDetailAsync"=>Task.FromResult<ProductionOrderDetail?>(Detail(1)),
            "GetIssuedGoldByPurityAsync"=>Task.FromResult<IReadOnlyList<IssuedGoldByPurityDto>>([new("750",2)]),
            "GetIssuedStoneByMaterialAsync"=>Task.FromResult<IReadOnlyList<IssuedStoneByMaterialDto>>([]),
            _=>throw new NotSupportedException()
        },()=>writes++);
        vm.SelectedOrder=new(1,"A");vm.RecordDebtCommand.Execute(null);
        Assert.True(vm.DebtRecorded);Assert.Equal(0,writes);
        vm.Products.Add(Item());Assert.False(vm.DebtRecorded);
        vm.RecordDebtCommand.Execute(null);Assert.True(vm.DebtRecorded);
        vm.GoldReconcileRows[0].LossWeightText="0.1";Assert.False(vm.DebtRecorded);
    }
    [Fact]
    public void Surplus_confirmation_warns_without_posting_material_stock()
    {
        var writes=0;var vm=Create((m,a)=>m.Name switch
        {
            "GetOrderDetailAsync"=>Task.FromResult<ProductionOrderDetail?>(Detail(1)),
            "GetIssuedGoldByPurityAsync"=>Task.FromResult<IReadOnlyList<IssuedGoldByPurityDto>>([new("750",.8m)]),
            "GetIssuedStoneByMaterialAsync"=>Task.FromResult<IReadOnlyList<IssuedStoneByMaterialDto>>([]),
            _=>throw new NotSupportedException()
        },()=>writes++);
        vm.SelectedOrder=new(1,"A");vm.Products.Add(Item());
        vm.RecordDebtCommand.Execute(null);
        Assert.True(vm.DebtRecorded);
        Assert.Contains("vượt lượng đã giao",vm.ReconcileMessage);
        Assert.Contains("không tự nhập",vm.ReconcileMessage);
        Assert.Equal(0,writes);
    }
    [Fact]
    public void Multiple_workers_require_explicit_debtor_and_summary_names_selected_worker()
    {
        var vm=Create((m,a)=>m.Name switch
        {
            "GetOrderDetailAsync"=>Task.FromResult<ProductionOrderDetail?>(Detail(1) with{Workers=[new(1,"NV1","Thợ A"),new(2,"NV2","Thợ B")]}),
            "GetIssuedGoldByPurityAsync"=>Task.FromResult<IReadOnlyList<IssuedGoldByPurityDto>>([new("750",2)]),
            "GetIssuedStoneByMaterialAsync"=>Task.FromResult<IReadOnlyList<IssuedStoneByMaterialDto>>([]),
            _=>throw new NotSupportedException()
        });
        vm.SelectedOrder=new(1,"LSX1");vm.Products.Add(Item());
        Assert.Null(vm.SelectedWorker);
        vm.RecordDebtCommand.Execute(null);Assert.False(vm.DebtRecorded);
        vm.SelectedWorker=vm.WorkerOptions[1];vm.RecordDebtCommand.Execute(null);
        Assert.True(vm.DebtRecorded);Assert.Contains("Thợ B",vm.BuildPostingSummary());
        Assert.Contains("nợ "+1.1m.ToString("0.####"),vm.BuildPostingSummary());
    }
    [Fact]
    public void Check_result_loads_saved_receipt_without_posting_again()
    {
        var calls=0;
        var documents=Stub<IFinishedReceiptDocuments>((m,a)=>Task.FromResult<ManualFinishedReceiptResult?>(new(7,"NTP7",1,new(DateTime.Today,1,1,[Item()],null,Guid.NewGuid(),[]))));
        var vm=new ProductionFinishedReceiptDialogViewModel(Stub<IWarehouseRepository>(),Stub<IProductionQueryRepository>((m,a)=>m.Name switch
        {
            "GetOrderDetailAsync"=>Task.FromResult<ProductionOrderDetail?>(Detail(1)),
            _=>throw new NotSupportedException()
        }),Stub<IProductionApplicationService>((m,a)=>{calls++;throw new NotSupportedException();}),new(1,"test","Test","ADMIN",1,"CN","CN"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>(),documents:documents);
        vm.SelectedOrder=new(1,"LSX1");
        vm.CheckResultCommand.Execute(null);
        Assert.True(vm.IsPosted);Assert.Contains("NTP7",vm.ReceiptState);Assert.Equal(0,calls);
    }
    [Fact]
    public void Progress_exposes_order_mismatch_and_updates_received_count()
    {
        var query=Stub<IProductionQueryRepository>((m,a)=>m.Name switch
        {
            "GetOrderDetailAsync"=>Task.FromResult<ProductionOrderDetail?>(Detail(1) with{SalesOrderId=10,Lines=[new(11,1,"M1","Nhẫn","750","750",2,100,1,1000,0,null,null)]}),
            "GetIssuedGoldByPurityAsync"=>Task.FromResult<IReadOnlyList<IssuedGoldByPurityDto>>([new("750",2)]),
            "GetIssuedStoneByMaterialAsync"=>Task.FromResult<IReadOnlyList<IssuedStoneByMaterialDto>>([]),
            _=>throw new NotSupportedException()
        });
        var craft=Stub<ICraftOrderApplicationService>((m,a)=>Task.FromResult<IReadOnlyList<CraftOrderLineItem>>([new(1,"Nhẫn","750",3,1,1000,100,0,3300,"M1",null,null,[])]));
        var vm=new ProductionFinishedReceiptDialogViewModel(Stub<IWarehouseRepository>(),query,Stub<IProductionApplicationService>(),new(1,"test","Test","ADMIN",1,"CN","CN"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>(),craftOrders:craft);
        vm.SelectedOrder=new(1,"LSX1");vm.Products.Add(Item() with{OrderLineId=11});
        var progress=Assert.Single(vm.LineProgress);Assert.Equal(3,progress.Ordered);Assert.Equal(1,progress.Missing);Assert.Equal("Lệch đơn đặt",progress.Status);
    }
    [Fact]
    public void Busy_and_posted_forms_block_mutation_and_pending_editor_cannot_be_lost()
    {
        var vm=Create((m,a)=>throw new NotSupportedException());
        typeof(ProductionFinishedReceiptDialogViewModel).GetProperty(nameof(vm.IsBusy))!.SetValue(vm,true);
        Assert.False(vm.AddCommand.CanExecute(null));Assert.False(vm.CanClose());
        typeof(ProductionFinishedReceiptDialogViewModel).GetProperty(nameof(vm.IsBusy))!.SetValue(vm,false);
        vm.ProductName="Món đang gõ";Assert.False(vm.CanClose());vm.SaveCommand.Execute(null);Assert.Contains("đang gõ",vm.ErrorMessage);
        var saved=new ManualFinishedReceiptResult(1,"NTP-1",1,new(DateTime.Today,1,1,[Item()],null,Guid.NewGuid(),[]));
        typeof(ProductionFinishedReceiptDialogViewModel).GetMethod("SetPosted",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,[saved]);
        Assert.True(vm.IsPosted);Assert.False(vm.SaveCommand.CanExecute(null));Assert.False(vm.AddCommand.CanExecute(null));Assert.True(vm.PrintReceiptCommand.CanExecute(null));Assert.True(vm.CanClose());
    }
    private static ManualFinishedProductItem Item()=>new("N1","N1","Nhẫn",null,"750","750","Chỉ",1,.1m,.9m,1000,2000,100,100,100,null,null,null,null,1);
    private static ProductionOrderDetail Detail(long id)=>JsonSerializer.Deserialize<ProductionOrderDetail>("""
        {"Id":1,"Code":"LSX","OrderSource":"STOCK","ExecutionType":"IN_HOUSE","StatusCode":"COMPLETED","Lines":[],"Workers":[{"EmployeeId":1,"EmployeeCode":"NV1","FullName":"Thợ"}],"Attachments":[],"MaterialBalances":[],"QcUnits":[]}
        """)! with{Id=id};
    private static ProductionFinishedReceiptDialogViewModel Create(Func<MethodInfo,object?[],object?> query,Action? writes=null)
        =>new(Stub<IWarehouseRepository>(),Stub<IProductionQueryRepository>(query),Stub<IProductionApplicationService>((m,a)=>{writes?.Invoke();throw new NotSupportedException();}),
            new(1,"test","Test","ADMIN",1,"CN","Chi nhánh"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>());
    private static T Stub<T>(Func<MethodInfo,object?[],object?>? action=null) where T:class
    {var stub=DispatchProxy.Create<T,ServiceProxy>();((ServiceProxy)(object)stub).Action=action;return stub;}
    public class ServiceProxy:DispatchProxy
    {
        public Func<MethodInfo,object?[],object?>? Action;
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>Action?.Invoke(method!,args??[])??throw new NotSupportedException();
    }
}
