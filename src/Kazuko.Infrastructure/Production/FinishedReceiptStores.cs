using System.Text.Json;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Production;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Production;

public sealed class JsonFinishedReceiptDraftStore(string? root=null) : IFinishedReceiptDraftStore
{
    private string _root=>root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KazukoERP","receipt-drafts",
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            FirebirdDatabase.Profile.Host+":"+FirebirdDatabase.Profile.Port+":"+Path.GetFullPath(FirebirdDatabase.Profile.DatabasePath).ToUpperInvariant())))[..24]);
    private string FilePath(long branch,long user,long order)
    {
        if(branch<=0||user<=0||order<=0)throw new InvalidOperationException("Nháp cần chi nhánh, người dùng và LSX hợp lệ.");
        return Path.Combine(_root,$"{branch}-{user}-{order}.json");
    }
    public FinishedReceiptDraft? Load(long branchId,long userId,long orderId)
    {var path=FilePath(branchId,userId,orderId);return File.Exists(path)?JsonSerializer.Deserialize<FinishedReceiptDraft>(File.ReadAllText(path)):null;}
    public void Save(long branchId,long userId,long orderId,FinishedReceiptDraft draft)
    {
        var path=FilePath(branchId,userId,orderId);Directory.CreateDirectory(_root);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(draft));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Delete(long branchId,long userId,long orderId){var path=FilePath(branchId,userId,orderId);if(File.Exists(path))File.Delete(path);}
}

public sealed class FirebirdFinishedReceiptDocuments : IFinishedReceiptDocuments
{
    public async Task<ManualFinishedReceiptResult?> GetByOrderAsync(long orderId,AuthenticatedSession session,CancellationToken cancellationToken=default)
    {
        if(session.BranchId<=0||orderId<=0)throw new InvalidOperationException("Thiếu chi nhánh hoặc LSX.");
        await using var c=FirebirdDatabase.CreateConnection();await c.OpenAsync(cancellationToken);
        var row=await c.QuerySingleOrDefaultAsync<StoredReceipt>(new CommandDefinition("""
            SELECT D.ID AS Id,D.CODE AS Code,R.PAYLOAD AS Payload FROM PRODUCTION_RECEIPT_REQUESTS R
            JOIN STOCK_DOCUMENTS D ON D.ID=R.DOCUMENT_ID
            WHERE D.PRODUCTION_ORDER_ID=@orderId AND D.BRANCH_ID=@branch
              AND D.COMPANY_ID=(SELECT ID FROM COMPANIES WHERE CODE='KAZUKO') AND D.STATUS_CODE='POSTED'
            """,new{orderId,branch=session.BranchId},cancellationToken:cancellationToken));
        if(row is null)return null;
        var request=JsonSerializer.Deserialize<PostManualFinishedReceiptRequest>(row.Payload)??throw new InvalidOperationException("Phiếu nhập thiếu snapshot.");
        return new(row.Id,row.Code,request.Items.Count,request);
    }
    private sealed record StoredReceipt(long Id,string Code,string Payload);
}
