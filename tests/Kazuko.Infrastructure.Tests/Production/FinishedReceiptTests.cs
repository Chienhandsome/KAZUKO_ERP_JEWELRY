using Kazuko.Core.Production;
using Kazuko.Infrastructure.Production;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.UserManagement;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Production;

public sealed class FinishedReceiptTests
{
    [Fact]
    public void Atomic_receipt_requires_both_stock_posting_and_gold_reconciliation_rights()
    {
        var stock=new GroupPermissionRow(1,"INV_FINISHED_RECEIPT",true,true,false,false,false);
        var reconcile=new GroupPermissionRow(1,"PROD_GOLD_RECONCILE",true,true,false,false,false);
        Assert.False(BackendAccess.Allows("IProductionApplicationService","PostManualFinishedReceiptAsync",[],new(false,[stock])));
        Assert.True(BackendAccess.Allows("IProductionApplicationService","PostManualFinishedReceiptAsync",[],new(false,[stock,reconcile])));
        Assert.False(BackendAccess.Allows(nameof(IFinishedReceiptDocuments),"GetByOrderAsync",[],PermissionSnapshot.Denied));
    }
    private static ManualFinishedProductItem Item()=>new("","","Nhẫn",null,"750","750","Chỉ",1,.1m,.9m,1000,2000,100,100,100,"NCC","M1",null,"Note",10);
    [Theory]
    [InlineData("1.000",true,1000)][InlineData("1000",true,1000)]
    [InlineData("-100",false,0)][InlineData("abc100",false,0)][InlineData("0.5",false,0)][InlineData("1,000",false,0)]
    public void Money_never_changes_invalid_input_into_another_value(string text,bool valid,decimal expected)
    {Assert.Equal(valid,FinishedReceiptValidation.TryMoney(text,out var value));Assert.Equal(expected,value);}
    [Fact]
    public void Item_checks_weight_equation_cost_and_labor()
    {
        FinishedReceiptValidation.ValidateItem(Item());
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateItem(Item() with{TotalWeight=0}));
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateItem(Item() with{GoldWeight=1}));
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateItem(Item() with{CostPrice=null}));
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateItem(Item() with{PurchaseLaborCost=1001}));
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateItem(Item() with{SalePrice=-1}));
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateItem(Item() with{SalePrice=1.5m}));
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateItem(Item() with{WeightUnitCode="GRAM"}));
    }
    [Fact]
    public void Quantities_must_match_each_line_not_only_order_total()
    {
        var plans=new Dictionary<long,int>{{10,1},{20,1}};
        FinishedReceiptValidation.ValidateQuantities(plans,[Item(),Item() with{OrderLineId=20}]);
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateQuantities(plans,[Item(),Item()]));
        Assert.Throws<InvalidOperationException>(()=>FinishedReceiptValidation.ValidateQuantities(plans,[Item()]));
    }
    [Fact]
    public void Durable_draft_preserves_retry_identity_and_scopes_by_branch_user_order()
    {
        var root=Path.Combine(Path.GetTempPath(),"receipt-tests",Guid.NewGuid().ToString("N"));
        try
        {
            var store=new JsonFinishedReceiptDraftStore(root);var id=Guid.NewGuid();
            var draft=new FinishedReceiptDraft(new(DateTime.Today,1,2,[Item()],"note",id,[new("750",.1m,3,"EMPLOYEE")]),10,true);
            store.Save(1,2,3,draft);var loaded=store.Load(1,2,3)!;
            Assert.Equal(id,loaded.Request.RequestId);Assert.True(loaded.PostingStarted);Assert.Equal(Item(),Assert.Single(loaded.Request.Items));
            Assert.Null(store.Load(2,2,3));Assert.Null(store.Load(1,3,3));Assert.Null(store.Load(1,2,4));
            Assert.Empty(Directory.GetFiles(root,"*.tmp"));store.Delete(1,2,3);Assert.Null(store.Load(1,2,3));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]
    public async Task Attachment_rollback_preserves_old_and_staging_files_then_retry_can_commit()
    {
        var root=Path.Combine(Path.GetTempPath(),"receipt-files",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var store=new LocalProductionAttachmentStore(root);var source=Path.Combine(root,"source.png");await File.WriteAllTextAsync(source,"sample");
            var staging=await store.ImportToStagingAsync(source);var old=await store.FinalizeAsync(staging,"OLD");staging=await store.ImportToStagingAsync(source);string copy;
            using(var tx=new ProductionAttachmentCommit(store)){copy=await tx.FinalizeAsync(staging,"NEW",default);tx.DeleteAfterCommit(old);}
            Assert.True(File.Exists(old));Assert.True(File.Exists(staging));Assert.False(File.Exists(copy));
            using(var tx=new ProductionAttachmentCommit(store)){copy=await tx.FinalizeAsync(staging,"NEW",default);tx.DeleteAfterCommit(old);tx.BeginCommit();tx.Commit();}
            Assert.True(File.Exists(copy));Assert.False(File.Exists(old));Assert.False(File.Exists(staging));
        }
        finally{Directory.Delete(root,true);}
    }
}
