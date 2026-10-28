using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Production;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Production;

public sealed partial class FirebirdProductionApplicationService
{
    private sealed record ReceiptRequestRow(long Id,string Code,string Payload,string RequestHash);
    private sealed record ReceiptPlannedLine(long Id,int Quantity,string GoldCode);
    private sealed record ReceiptIssuedGold(string Code,decimal Weight);
    private static async Task PostReceiptGoldSettlementsAsync(FbConnection connection,FbTransaction transaction,
        PostManualFinishedReceiptRequest request,AuthenticatedSession session,string orderCode,CancellationToken cancellationToken)
    {
        if(request.GoldSettlements is null)throw new InvalidOperationException("Chưa xác nhận đối chiếu vàng của phiếu nhập.");
        var issued=(await connection.QueryAsync<ReceiptIssuedGold>(new CommandDefinition("""
            SELECT M.PURITY_CODE AS Code,SUM(B.QUANTITY_ISSUED-B.QUANTITY_RETURNED-B.QUANTITY_SCRAP) AS Weight
            FROM PRODUCTION_MATERIAL_BALANCES B JOIN MATERIALS M ON M.ID=B.MATERIAL_ID
            WHERE B.PRODUCTION_ORDER_ID=@id AND M.MATERIAL_TYPE='GOLD' GROUP BY M.PURITY_CODE
            """,new{id=request.ProductionOrderId},transaction,cancellationToken:cancellationToken))).ToDictionary(x=>x.Code,x=>x.Weight,StringComparer.OrdinalIgnoreCase);
        var received=request.Items.GroupBy(x=>x.GoldPurityCode!,StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.Sum(x=>x.GoldWeight),StringComparer.OrdinalIgnoreCase);
        var codes=issued.Keys.Union(received.Keys,StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(request.GoldSettlements.GroupBy(x=>x.GoldPurityCode,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()!=1)||
           !codes.SetEquals(request.GoldSettlements.Select(x=>x.GoldPurityCode)))
            throw new InvalidOperationException("Đối chiếu chưa đủ từng loại vàng; mở đối chiếu và xác nhận lại.");
        // Hao hụt thợ đã báo ở bước trả hàng (QUANTITY_LOSS): 1 lần đối chiếu dùng
        // chung 1 con số. Dialog đã prefill số này; nếu người dùng sửa khác thì vẫn
        // cho sửa nhưng ghi rõ vào audit log để truy vết.
        var reportedLoss=(await connection.QueryAsync<ReceiptIssuedGold>(new CommandDefinition("""
            SELECT M.PURITY_CODE AS Code,COALESCE(SUM(B.QUANTITY_LOSS),0) AS Weight
            FROM PRODUCTION_MATERIAL_BALANCES B JOIN MATERIALS M ON M.ID=B.MATERIAL_ID
            WHERE B.PRODUCTION_ORDER_ID=@id AND M.MATERIAL_TYPE='GOLD' GROUP BY M.PURITY_CODE
            """,new{id=request.ProductionOrderId},transaction,cancellationToken:cancellationToken))).ToDictionary(x=>x.Code,x=>x.Weight,StringComparer.OrdinalIgnoreCase);
        foreach(var row in request.GoldSettlements)
        {
            var weightIssued=issued.GetValueOrDefault(row.GoldPurityCode);
            var weightReceived=received.GetValueOrDefault(row.GoldPurityCode);
            var balance=weightIssued-weightReceived;
            if(row.LossWeightChi<0||row.LossWeightChi>Math.Max(balance,0))
                throw new InvalidOperationException($"{row.GoldPurityCode}: hao hụt không hợp lệ hoặc vượt phần chênh lệch.");
            var lossReported=reportedLoss.GetValueOrDefault(row.GoldPurityCode);
            if(row.LossWeightChi!=lossReported)
                await AuditLogWriter.WriteAsync(connection,transaction,"GOLD_LOSS_ADJUSTED",
                    session.UserId,session.Username,session.BranchId,
                    "PRODUCTION_ORDER",request.ProductionOrderId!.Value.ToString(),
                    $"{orderCode}|{row.GoldPurityCode}|reported:{lossReported:0.####}|entered:{row.LossWeightChi:0.####}",cancellationToken);
            var debt=balance-row.LossWeightChi;
            if(debt<=.0001m)continue;
            if(row.WorkerId is null or <=0)throw new InvalidOperationException("Chọn thợ chịu nợ vàng trước khi ghi nhập kho.");
            string? workerName;
            if(row.WorkerType==WorkerGoldDebtWorkerTypes.ExternalWorker)
                workerName=await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                    SELECT W.NAME FROM EXTERNAL_WORKERS W JOIN PRODUCTION_ORDERS P ON P.EXTERNAL_WORKER_ID=W.ID
                    WHERE P.ID=@id AND P.EXECUTION_TYPE='OUTSOURCED' AND W.ID=@worker AND W.IS_ACTIVE=TRUE
                    """,new{id=request.ProductionOrderId,worker=row.WorkerId},transaction,cancellationToken:cancellationToken));
            else if(row.WorkerType==WorkerGoldDebtWorkerTypes.Employee)
                workerName=await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                    SELECT E.FULL_NAME FROM HR_EMPLOYEES E JOIN PRODUCTION_ORDER_WORKERS W ON W.EMPLOYEE_ID=E.ID
                    WHERE W.PRODUCTION_ORDER_ID=@id AND E.ID=@worker
                    """,new{id=request.ProductionOrderId,worker=row.WorkerId},transaction,cancellationToken:cancellationToken));
            else throw new InvalidOperationException("Loại thợ không hợp lệ.");
            if(workerName is null)throw new InvalidOperationException("Thợ không thuộc LSX hoặc không còn hoạt động.");
            await InsertReceiptGoldDebtAsync(connection,transaction,new(row.WorkerId.Value,workerName,row.GoldPurityCode,
                debt,weightIssued,weightReceived,row.LossWeightChi,request.ProductionOrderId!.Value,orderCode,null,row.WorkerType!),session,cancellationToken);
        }
    }
}
