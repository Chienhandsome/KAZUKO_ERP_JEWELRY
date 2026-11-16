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
    public void Single_model_is_selected_on_load_and_after_clearing_the_editor()
    {
        var vm=Create(ModelQuery);
        var changes=new List<string?>();
        vm.PropertyChanged+=(_,e)=>changes.Add(e.PropertyName);
        vm.SelectedOrder=new(1,"A");
        Assert.Equal("Nhẫn",vm.SelectedLine!.ModelName);Assert.False(vm.ShowLineSelector);Assert.False(vm.ShowCancelEditor);
        vm.TotalWeightText="1";
        Assert.True(vm.ShowCancelEditor);Assert.Contains(nameof(vm.ShowCancelEditor),changes);
        vm.CancelEditCommand.Execute(null);
        Assert.NotNull(vm.SelectedLine);Assert.Equal("Nhẫn",vm.ProductName);Assert.False(vm.ShowCancelEditor);
        vm.SelectedOrder=new(2,"B");
        Assert.Equal(2,vm.SelectedOrder.Id);Assert.True(vm.ShowLineSelector);Assert.Null(vm.SelectedLine);Assert.Equal("",vm.ProductName);
    }

    [Fact]
    public void Multiple_models_still_require_a_choice()
    {
        var vm=Create(ModelQuery);vm.SelectedOrder=new(2,"B");
        Assert.True(vm.ShowLineSelector);Assert.Null(vm.SelectedLine);
        vm.SelectedLine=vm.OrderLines[1];
        Assert.Equal("Dây chuyền",vm.ProductName);Assert.False(vm.ShowCancelEditor);
    }

    [Fact]
    public void Result_check_is_shown_after_post_failure_and_hidden_after_success()
    {
        var vm=Create((m,a)=>throw new NotSupportedException());
        Assert.False(vm.ShowResultCheck);
        var request=new PostManualFinishedReceiptRequest(DateTime.Today,1,1,[Item()],null,Guid.NewGuid(),[]);
        typeof(ProductionFinishedReceiptDialogViewModel).GetField("_pendingPost",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,request);
        Assert.True(vm.ShowResultCheck);
        vm.SaveCommand.Execute(null); // The fake application rejects the attempted post.
        Assert.True(vm.ShowResultCheck);
        typeof(ProductionFinishedReceiptDialogViewModel).GetMethod("SetPosted",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,[new ManualFinishedReceiptResult(1,"NTP1",1,request)]);
        Assert.False(vm.ShowResultCheck);Assert.False(vm.ShowCancelEditor);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Delivery_shortcut_requires_a_posted_receipt_linked_to_a_sales_order(bool linked)
    {
        var vm=new ProductionFinishedReceiptDialogViewModel(Stub<IWarehouseRepository>(),Stub<IProductionQueryRepository>(),Stub<IProductionApplicationService>(),
            new(1,"test","Test","ADMIN",1,"CN","CN"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>(),openDelivery:_=>Task.CompletedTask);
        typeof(ProductionFinishedReceiptDialogViewModel).GetField("_detail",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,Detail(1) with{SalesOrderId=linked?10:null});
        Assert.False(vm.ShowDelivery);
        var request=new PostManualFinishedReceiptRequest(DateTime.Today,1,1,[Item()],null,Guid.NewGuid(),[]);
        typeof(ProductionFinishedReceiptDialogViewModel).GetMethod("SetPosted",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,[new ManualFinishedReceiptResult(1,"NTP1",1,request)]);
        Assert.Equal(linked,vm.ShowDelivery);
    }

    private static object ModelQuery(MethodInfo method,object?[] args)=>method.Name switch
    {
        "GetOrderDetailAsync"=>Task.FromResult<ProductionOrderDetail?>(Detail((long)args[0]!) with{Lines=(long)args[0]! == 1
            ?[new(11,1,"M1","Nhẫn","750","750",2,100,1,1000,0,null,null)]
            :[new(11,1,"M1","Nhẫn","750","750",2,100,1,1000,0,null,null),new(12,2,"M2","Dây chuyền","9999","9999",1,100,1,1000,0,null,null)]}),
        "GetIssuedGoldByPurityAsync"=>Task.FromResult<IReadOnlyList<IssuedGoldByPurityDto>>([]),
        "GetIssuedStoneByMaterialAsync"=>Task.FromResult<IReadOnlyList<IssuedStoneByMaterialDto>>([]),
        _=>throw new NotSupportedException()
    };
    [Fact]
    public void Selecting_a_model_only_does_not_block_cancel()
    {
        var vm=Create((m,a)=>throw new NotSupportedException(),confirmClose:(message,buttons)=>throw new Exception("No confirmation expected."));
        vm.SelectedLine=new(29,1,"M1","Lắc tay 9999","9999","Vàng 9999",10,50000,.5m,1000,0,null,null);
        Assert.False(vm.HasPendingEditor);
        vm.CancelCommand.Execute(null);
        Assert.Equal(false,vm.DialogResult);
        Assert.True(vm.CanClose()); // Window.Closing runs after the command.
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dirty_editor_can_be_discarded_or_kept_when_closing(bool discard)
    {
        var prompts=0;
        var vm=Create((m,a)=>throw new NotSupportedException(),confirmClose:(message,buttons)=>
        {
            prompts++;Assert.Equal(System.Windows.MessageBoxButton.YesNo,buttons);
            return discard?System.Windows.MessageBoxResult.Yes:System.Windows.MessageBoxResult.No;
        });
        vm.ProductName="Món đang nhập";vm.QuantityText="10";
        vm.CancelCommand.Execute(null);
        Assert.Equal(1,prompts);
        if(discard){Assert.Equal(false,vm.DialogResult);Assert.False(vm.HasPendingEditor);Assert.True(vm.CanClose());Assert.Equal(1,prompts);}
        else {Assert.Null(vm.DialogResult);Assert.Equal("Món đang nhập",vm.ProductName);Assert.Equal("10",vm.QuantityText);}
    }

    [Fact]
    public void Cancelling_draft_question_keeps_the_editor_and_added_products()
    {
        var prompts=new Queue<System.Windows.MessageBoxResult>([System.Windows.MessageBoxResult.Yes,System.Windows.MessageBoxResult.Cancel]);
        var vm=Create((m,a)=>throw new NotSupportedException(),confirmClose:(message,buttons)=>prompts.Dequeue());
        vm.Products.Add(Item());vm.ProductName="Chưa thêm";
        Assert.False(vm.CanClose());
        Assert.Equal("Chưa thêm",vm.ProductName);Assert.Single(vm.Products);Assert.Empty(prompts);
    }

    [Fact]
    public void Failed_draft_save_keeps_the_editor_and_window_open()
    {
        var vm=Create((m,a)=>throw new NotSupportedException(),confirmClose:(message,buttons)=>System.Windows.MessageBoxResult.Yes);
        vm.Products.Add(Item());vm.ProductName="Chưa thêm";
        Assert.False(vm.CanClose());
        Assert.Equal("Chưa thêm",vm.ProductName);Assert.Single(vm.Products);Assert.Contains("lưu nháp",vm.ErrorMessage);
    }

    [Fact]
    public void Closing_can_discard_editor_and_save_only_added_products_as_draft()
    {
        var store=new MemoryDraftStore();
        var vm=new ProductionFinishedReceiptDialogViewModel(Stub<IWarehouseRepository>(),Stub<IProductionQueryRepository>(),
            Stub<IProductionApplicationService>(),new(1,"test","Test","ADMIN",1,"CN","CN"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>(),
            draftStore:store,confirmClose:(message,buttons)=>System.Windows.MessageBoxResult.Yes);
        vm.SelectedOrder=new(1,"LSX1");vm.Products.Add(Item());vm.ProductName="Chưa thêm";
        vm.CancelCommand.Execute(null);
        Assert.Equal(false,vm.DialogResult);Assert.False(vm.HasPendingEditor);
        Assert.Equal("Nhẫn",Assert.Single(store.Saved!.Request.Items).ProductName);
        Assert.True(vm.CanClose());
    }

    [Fact]
    public void Uncertain_post_result_cannot_be_discarded_when_closing()
    {
        var answers=new Queue<System.Windows.MessageBoxResult>([System.Windows.MessageBoxResult.Yes,System.Windows.MessageBoxResult.No]);
        var vm=Create((m,a)=>throw new NotSupportedException(),confirmClose:(message,buttons)=>answers.Dequeue());
        vm.Products.Add(Item());vm.ProductName="Chưa thêm";
        var request=new PostManualFinishedReceiptRequest(DateTime.Today,1,1,[Item()],null,Guid.NewGuid(),[]);
        typeof(ProductionFinishedReceiptDialogViewModel).GetField("_pendingPost",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,request);
        Assert.False(vm.CanClose());Assert.Equal("Chưa thêm",vm.ProductName);Assert.Single(vm.Products);
        Assert.Contains("chưa rõ kết quả",vm.ErrorMessage);
    }

    [Theory]
    [InlineData("QuantityText","10")]
    [InlineData("StoneWeightText","0.1")]
    [InlineData("SalePriceText","50000")]
    [InlineData("FormNotes","Ghi chú đang nhập")]
    public void Non_name_editor_changes_are_not_silently_lost(string property,string value)
    {
        var vm=Create((m,a)=>throw new NotSupportedException());
        typeof(ProductionFinishedReceiptDialogViewModel).GetProperty(property)!.SetValue(vm,value);
        Assert.True(vm.HasPendingEditor);Assert.False(vm.CanClose());
        Assert.Equal(value,typeof(ProductionFinishedReceiptDialogViewModel).GetProperty(property)!.GetValue(vm));
    }

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
    private static ProductionFinishedReceiptDialogViewModel Create(Func<MethodInfo,object?[],object?> query,Action? writes=null,
        Func<string,System.Windows.MessageBoxButton,System.Windows.MessageBoxResult>? confirmClose=null)
        =>new(Stub<IWarehouseRepository>(),Stub<IProductionQueryRepository>(query),Stub<IProductionApplicationService>((m,a)=>{writes?.Invoke();throw new NotSupportedException();}),
            new(1,"test","Test","ADMIN",1,"CN","Chi nhánh"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>(),
            confirmClose:confirmClose??((message,buttons)=>System.Windows.MessageBoxResult.No));
    private static T Stub<T>(Func<MethodInfo,object?[],object?>? action=null) where T:class
    {var stub=DispatchProxy.Create<T,ServiceProxy>();((ServiceProxy)(object)stub).Action=action;return stub;}
    public class ServiceProxy:DispatchProxy
    {
        public Func<MethodInfo,object?[],object?>? Action;
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>Action?.Invoke(method!,args??[])??throw new NotSupportedException();
    }
    private sealed class MemoryDraftStore:IFinishedReceiptDraftStore
    {
        public FinishedReceiptDraft? Saved;
        public FinishedReceiptDraft? Load(long branchId,long userId,long orderId)=>null;
        public void Save(long branchId,long userId,long orderId,FinishedReceiptDraft draft)=>Saved=draft;
        public void Delete(long branchId,long userId,long orderId)=>Saved=null;
    }
}
