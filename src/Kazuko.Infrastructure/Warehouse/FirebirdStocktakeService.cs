using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdStocktakeService : IStocktakeService
{
    public async Task<IReadOnlyList<WarehouseLocationOption>> GetLocationsAsync(AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(cancellationToken);
        return (await c.QueryAsync<WarehouseLocationOption>(new CommandDefinition("""
            SELECT L.ID Id,L.BRANCH_ID BranchId,L.CODE Code,L.NAME Name,L.LOCATION_TYPE LocationType,L.INVENTORY_SCOPE InventoryScope
            FROM LOCATIONS L JOIN BRANCHES B ON B.ID=L.BRANCH_ID
            WHERE L.BRANCH_ID=@BranchId AND L.IS_ACTIVE=TRUE AND B.IS_ACTIVE=TRUE
              AND L.INVENTORY_SCOPE IN ('RAW_MATERIAL','FINISHED_GOODS','SALES_FLOOR') ORDER BY L.NAME
            """, session, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<StocktakeSummary>> ListAsync(AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(cancellationToken);
        return (await c.QueryAsync<StocktakeSummary>(new CommandDefinition("""
            SELECT H.ID Id,H.CODE Code,H.COUNT_DATE CountDate,L.NAME LocationName,H.SCOPE_CODE Scope,H.STATUS_CODE Status,
              H.REVISION Revision,COALESCE(U.DISPLAY_NAME,U.USERNAME,'') CreatedByName
            FROM INV_COUNTS H JOIN LOCATIONS L ON L.ID=H.LOCATION_ID LEFT JOIN USERS U ON U.ID=H.CREATED_BY
            JOIN BRANCHES B ON B.ID=H.BRANCH_ID
            WHERE H.BRANCH_ID=@BranchId AND H.COMPANY_ID=B.COMPANY_ID ORDER BY H.ID DESC
            """, session, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<StocktakeDetail> GetAsync(long id, AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var t = c.BeginTransaction(IsolationLevel.ReadCommitted);
        var h = await Header(c,t,id,session,cancellationToken);
        return await Detail(c,t,h,cancellationToken);
    }

    public Task<StocktakeDetail> CreateAsync(StocktakeCreateRequest request, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Write(session, cancellationToken, async (c,t) =>
        {
            if (request.RequestId == Guid.Empty || request.CountDate.Year < 1900 || request.CountDate.Year > 9998)
                throw new InvalidOperationException("Ngày kiểm kê hoặc mã yêu cầu không hợp lệ.");
            Text(request.Notes,1000,"Ghi chú phiếu");
            var l = await Location(c,t,request.LocationId,session,cancellationToken);
            var existing = await c.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                "SELECT ID FROM INV_COUNTS WHERE COMPANY_ID=@Company AND REQUEST_ID=@RequestId",
                new { l.Company, RequestId=request.RequestId.ToString() },t,cancellationToken:cancellationToken));
            if (existing is { } found)
            {
                var previous = await Header(c,t,found,session,cancellationToken);
                if(previous.LocationId!=request.LocationId || previous.CountDate.Date!=request.CountDate.Date || previous.Notes!=request.Notes)
                    throw new InvalidOperationException("Yêu cầu tạo phiếu đã được dùng với dữ liệu khác.");
                return await Detail(c,t,previous,cancellationToken);
            }
            var scope = l.Scope == InventoryScopes.RawMaterial ? "MATERIALS" : "FINISHED";
            var lines = await Snapshot(c,t,l,session,cancellationToken);
            var code = await StockPostingSupport.NextDocumentCodeAsync(c,t,"STOCKTAKE_COUNT",session.BranchId,"KK",cancellationToken);
            var id = await c.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT INTO INV_COUNTS(COMPANY_ID,BRANCH_ID,LOCATION_ID,CODE,COUNT_DATE,SCOPE_CODE,INVENTORY_REV,REQUEST_ID,NOTES,INITIAL_SNAPSHOT,CREATED_BY)
                VALUES(@Company,@BranchId,@LocationId,@code,@CountDate,@scope,@Version,@RequestId,@Notes,NULL,@UserId) RETURNING ID
                """, new { l.Company,session.BranchId,request.LocationId,code,CountDate=request.CountDate.Date,scope,l.Version,
                    RequestId=request.RequestId.ToString(),request.Notes,session.UserId },t,cancellationToken:cancellationToken));
            await using(var snapshotCommand=new FbCommand("UPDATE INV_COUNTS SET INITIAL_SNAPSHOT=@snapshot WHERE ID=@id",c,t))
            {
                snapshotCommand.Parameters.Add("@snapshot",FbDbType.Text).Value=JsonSerializer.Serialize(lines);
                snapshotCommand.Parameters.Add("@id",FbDbType.BigInt).Value=id;
                await snapshotCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach(var line in lines) await Insert(c,t,id,line,cancellationToken);
            await Audit(c,t,id,"STOCKTAKE_CREATED",session,new {code,request.LocationId,InventoryRevision=l.Version,Lines=lines.Count},cancellationToken);
            return await Detail(c,t,await Header(c,t,id,session,cancellationToken),cancellationToken);
        });

    public Task<StocktakeDetail> SaveAsync(StocktakeSaveRequest request, AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        Write(session,cancellationToken,async(c,t) =>
        {
            var h=await Editable(c,t,request.Id,request.Revision,session,cancellationToken);
            Text(request.Notes,1000,"Ghi chú phiếu");
            var before=await Lines(c,t,h.Id,cancellationToken);
            if(request.Lines.Count!=before.Count || request.Lines.Select(x=>x.LineId).Distinct().Count()!=before.Count ||
               request.Lines.Any(x=>before.All(b=>b.Id!=x.LineId))) throw new InvalidOperationException("Danh sách kiểm kê đã thay đổi. Hãy tải lại phiếu.");
            foreach(var entry in request.Lines)
            {
                var line=before.Single(x=>x.Id==entry.LineId);
                StocktakeValidation.ValidateQuantity(line.Kind,entry.Quantity); Text(entry.Notes,500,"Ghi chú dòng");
                await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE INV_COUNT_LINES SET COUNTED_QTY=@Quantity,NOTES=@Notes,
                      NEEDS_RECOUNT=CASE WHEN @Confirmed=TRUE AND @Quantity IS NOT NULL THEN FALSE ELSE NEEDS_RECOUNT END
                    WHERE ID=@LineId AND COUNT_ID=@Id
                    """, new { entry.Quantity,entry.Notes,entry.LineId,request.Id,Confirmed=entry.ConfirmRecount },t,cancellationToken:cancellationToken));
            }
            await Touch(c,t,h,session,cancellationToken,request.Notes);
            await Audit(c,t,h.Id,"STOCKTAKE_SAVED",session,new {Before=before,After=request},cancellationToken);
            return await Detail(c,t,await Header(c,t,h.Id,session,cancellationToken),cancellationToken);
        });

    public Task<StocktakeDetail> ScanAsync(long id,int revision,string scan,AuthenticatedSession session,CancellationToken cancellationToken=default) =>
        Write(session,cancellationToken,async(c,t)=>
        {
            var h=await Editable(c,t,id,revision,session,cancellationToken);
            if(h.Scope!="FINISHED") throw new InvalidOperationException("Quét serial chỉ dùng cho kho thành phẩm/kho bán hàng.");
            scan=(scan??"").Trim(); if(scan.Length==0)throw new InvalidOperationException("Chưa nhập mã serial."); Text(scan,500,"Mã quét");
            var matches=(await c.QueryAsync<SnapshotRow>(new CommandDefinition("""
                SELECT F.ID ItemId,F.CODE Code,F.PRODUCT_NAME Name,'món' Unit,F.TOTAL_COST Cost,F.COUNT_TOKEN Version,
                  COALESCE(F.LOCATION_ID,0) Location,F.STATUS_CODE State
                FROM FINISHED_PRODUCTS F WHERE F.COMPANY_ID=@Company AND F.BRANCH_ID=@BranchId
                  AND (F.CODE=@scan OR F.BARCODE_VALUE=@scan OR F.QR_VALUE=@scan)
                """,new {h.Company,session.BranchId,scan},t,cancellationToken:cancellationToken))).AsList();
            if(matches.Count>1)throw new InvalidOperationException("Mã quét khớp nhiều sản phẩm. Hãy dùng mã serial duy nhất.");
            var current=await Lines(c,t,id,cancellationToken);
            StocktakeLine candidate;
            if(matches.Count==1)
            {
                var item=matches[0]; item.Quantity=item.Location==h.LocationId && Available(item.State) ? 1:0;
                candidate=ToLine(item,"SERIAL");
            }
            else candidate=new(0,"U:"+Hash(scan),"UNKNOWN",null,scan,"Serial chưa xác định","món",0,1,"",Hash(scan),"UNKNOWN");
            var old=current.SingleOrDefault(x=>x.ItemKey==candidate.ItemKey);
            if(old is not null)
                await c.ExecuteAsync(new CommandDefinition("UPDATE INV_COUNT_LINES SET COUNTED_QTY=1,NEEDS_RECOUNT=FALSE WHERE ID=@Id",new{old.Id},t,cancellationToken:cancellationToken));
            else await Insert(c,t,id,candidate with {CountedQuantity=1},cancellationToken);
            await Touch(c,t,h,session,cancellationToken);
            await Audit(c,t,id,"STOCKTAKE_SCANNED",session,new{scan,candidate.ItemKey,Duplicate=old?.CountedQuantity==1},cancellationToken);
            return await Detail(c,t,await Header(c,t,id,session,cancellationToken),cancellationToken);
        });

    public Task<StocktakeDetail> ReviewAsync(long id,int revision,AuthenticatedSession session,CancellationToken cancellationToken=default) =>
        Write(session,cancellationToken,async(c,t)=>
        {
            var h=await Editable(c,t,id,revision,session,cancellationToken);
            var location=await Location(c,t,h.LocationId,session,cancellationToken);
            var before=await Lines(c,t,id,cancellationToken);
            var fresh=await Snapshot(c,t,location,session,cancellationToken);
            foreach(var old in before.Where(x=>x.Kind!="UNKNOWN" && fresh.All(n=>n.ItemKey!=x.ItemKey)))
            {
                if(old.Kind=="MATERIAL")
                {
                    fresh.Add(old with {SystemQuantity=0,Stamp=Hash("ABSENT"),ItemState="ABSENT"});
                    continue;
                }
                var r=await c.QuerySingleOrDefaultAsync<SnapshotRow>(new CommandDefinition("""
                    SELECT F.ID ItemId,F.CODE Code,F.PRODUCT_NAME Name,'món' Unit,F.TOTAL_COST Cost,F.COUNT_TOKEN Version,
                      COALESCE(F.LOCATION_ID,0) Location,F.STATUS_CODE State
                    FROM FINISHED_PRODUCTS F WHERE F.ID=@ItemId AND F.COMPANY_ID=@Company AND F.BRANCH_ID=@BranchId
                    """,new{old.ItemId,h.Company,session.BranchId},t,cancellationToken:cancellationToken));
                fresh.Add(r is not null ? ToLine(r,"SERIAL") with {SystemQuantity=0} : old with {SystemQuantity=0,Stamp=Hash("ABSENT"),ItemState="ABSENT"});
            }
            var changed=new List<string>();
            foreach(var line in fresh)
            {
                var old=before.SingleOrDefault(x=>x.ItemKey==line.ItemKey);
                if(old is null) {await Insert(c,t,id,line with {NeedsRecount=true},cancellationToken);changed.Add(line.Code);}
                else if(old.Stamp!=line.Stamp || old.SystemQuantity!=line.SystemQuantity)
                {
                    // Require recount after any stock change, even if the quantity later returned to its old value.
                    await c.ExecuteAsync(new CommandDefinition("""
                        UPDATE INV_COUNT_LINES SET SYSTEM_QTY=@SystemQuantity,COUNTED_QTY=@counted,STAMP=@Stamp,ITEM_STATE=@ItemState,NEEDS_RECOUNT=TRUE WHERE ID=@Id
                        """,new{line.SystemQuantity,counted=line.Kind=="MATERIAL"?(decimal?)null:0m,line.Stamp,line.ItemState,old.Id},t,cancellationToken:cancellationToken));
                    changed.Add(old.Code);
                }
            }
            await c.ExecuteAsync(new CommandDefinition("UPDATE INV_COUNTS SET INVENTORY_REV=@Version WHERE ID=@id",new{location.Version,id},t,cancellationToken:cancellationToken));
            await Touch(c,t,h,session,cancellationToken);
            await Audit(c,t,id,"STOCKTAKE_REVIEWED",session,new{OldInventoryRevision=h.InventoryRevision,NewInventoryRevision=location.Version,Changed=changed,Before=before},cancellationToken);
            return await Detail(c,t,await Header(c,t,id,session,cancellationToken),cancellationToken);
        });

    public Task<StocktakeDetail> CloseAsync(long id,int revision,AuthenticatedSession session,CancellationToken cancellationToken=default) =>
        Write(session,cancellationToken,async(c,t)=>
        {
            var h=await Editable(c,t,id,revision,session,cancellationToken);
            var location=await Location(c,t,h.LocationId,session,cancellationToken);
            if(location.Version!=h.InventoryRevision)throw new InvalidOperationException("Tồn kho đã thay đổi. Bấm Đối chiếu tồn, kiểm lại các dòng bị thay đổi rồi lưu/chốt lại.");
            var lines=await Lines(c,t,id,cancellationToken);
            if(lines.Count==0)throw new InvalidOperationException("Phiếu chưa có mặt hàng để kiểm kê.");
            if(lines.Any(x=>x.Kind=="MATERIAL"&&x.CountedQuantity is null))throw new InvalidOperationException("Cần nhập số thực tế cho tất cả nguyên vật liệu trước khi chốt.");
            if(lines.Any(x=>x.NeedsRecount))throw new InvalidOperationException("Còn dòng Cần kiểm lại. Hãy nhập lại/quét lại hoặc xác nhận không thấy từng dòng trước khi chốt.");
            await c.ExecuteAsync(new CommandDefinition("""
                UPDATE INV_COUNTS SET STATUS_CODE='COUNTED',REVISION=REVISION+1,CLOSED_BY=@UserId,CLOSED_AT=CURRENT_TIMESTAMP,
                 UPDATED_BY=@UserId,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID=@id
                """,new{id,session.UserId},t,cancellationToken:cancellationToken));
            await Audit(c,t,id,"STOCKTAKE_CLOSED",session,new{h.InventoryRevision,Lines=lines},cancellationToken);
            return await Detail(c,t,await Header(c,t,id,session,cancellationToken),cancellationToken);
        });

    private static async Task<StocktakeDetail> Write(AuthenticatedSession session,CancellationToken ct,
        Func<FbConnection,FbTransaction,Task<StocktakeDetail>> action)
    {
        ArgumentNullException.ThrowIfNull(session); StockPostingSupport.EnsureBranch(session);
        if(session.UserId<=0)throw new InvalidOperationException("Phiên làm việc thiếu người dùng.");
        await using var c=FirebirdDatabase.CreateConnection();await c.OpenAsync(ct);
        await using var t=c.BeginTransaction(IsolationLevel.ReadCommitted);
        var result=await action(c,t);await t.CommitAsync(ct);return result;
    }
    private static async Task<LocationRow> Location(FbConnection c,FbTransaction t,long id,AuthenticatedSession s,CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<LocationRow>(new CommandDefinition("""
            SELECT L.ID Id,(SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId) Company,L.INVENTORY_SCOPE Scope,L.COUNT_REV Version FROM LOCATIONS L
            WHERE L.ID=@id AND L.BRANCH_ID=@BranchId AND L.IS_ACTIVE=TRUE
             AND EXISTS(SELECT 1 FROM BRANCHES WHERE ID=@BranchId AND IS_ACTIVE=TRUE)
             AND L.INVENTORY_SCOPE IN ('RAW_MATERIAL','FINISHED_GOODS','SALES_FLOOR') WITH LOCK
            """,new{id,s.BranchId},t,cancellationToken:ct))??throw new InvalidOperationException("Kho không còn hoạt động hoặc không thuộc chi nhánh hiện tại.");
    private static async Task<HeaderRow> Header(FbConnection c,FbTransaction t,long id,AuthenticatedSession s,CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<HeaderRow>(new CommandDefinition("""
            SELECT H.ID Id,H.COMPANY_ID Company,H.CODE Code,H.COUNT_DATE CountDate,H.LOCATION_ID LocationId,L.NAME LocationName,
             H.SCOPE_CODE Scope,H.STATUS_CODE Status,H.REVISION Revision,H.INVENTORY_REV InventoryRevision,H.NOTES Notes,
             COALESCE(U.DISPLAY_NAME,U.USERNAME,'') CreatedByName
            FROM INV_COUNTS H JOIN LOCATIONS L ON L.ID=H.LOCATION_ID LEFT JOIN USERS U ON U.ID=H.CREATED_BY
            WHERE H.ID=@id AND H.BRANCH_ID=@BranchId AND H.COMPANY_ID=(SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId)
            """,new{id,s.BranchId},t,cancellationToken:ct))??throw new InvalidOperationException("Không tìm thấy phiếu kiểm kê trong chi nhánh.");
    private static async Task<HeaderRow> Editable(FbConnection c,FbTransaction t,long id,int revision,AuthenticatedSession s,CancellationToken ct)
    {
        await c.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT ID FROM INV_COUNTS WHERE ID=@id AND BRANCH_ID=@BranchId WITH LOCK",new{id,s.BranchId},t,cancellationToken:ct));
        var h=await Header(c,t,id,s,ct);
        if(h.Status!="DRAFT")throw new InvalidOperationException("Phiếu đã chốt, không thể sửa kết quả kiểm kê.");
        if(h.Revision!=revision)throw new InvalidOperationException("Phiếu đã được người khác cập nhật. Hãy tải lại trước khi tiếp tục.");
        return h;
    }
    private static async Task<List<StocktakeLine>> Lines(FbConnection c,FbTransaction t,long id,CancellationToken ct) =>
        (await c.QueryAsync<StocktakeLine>(new CommandDefinition("""
            SELECT ID Id,ITEM_KEY ItemKey,KIND Kind,ITEM_ID ItemId,ITEM_CODE Code,ITEM_NAME Name,UNIT_CODE Unit,
              SYSTEM_QTY SystemQuantity,COUNTED_QTY CountedQuantity,NOTES Notes,STAMP Stamp,ITEM_STATE ItemState,NEEDS_RECOUNT NeedsRecount
            FROM INV_COUNT_LINES WHERE COUNT_ID=@id ORDER BY KIND,ITEM_CODE,ID
            """,new{id},t,cancellationToken:ct))).AsList();
    private static async Task<StocktakeDetail> Detail(FbConnection c,FbTransaction t,HeaderRow h,CancellationToken ct)=>
        new(h.Id,h.Code,h.CountDate,h.LocationId,h.LocationName,h.Scope,h.Status,h.Revision,h.InventoryRevision,h.Notes,h.CreatedByName,await Lines(c,t,h.Id,ct));
    private static Task Insert(FbConnection c,FbTransaction t,long id,StocktakeLine line,CancellationToken ct)=>
        c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO INV_COUNT_LINES(COUNT_ID,ITEM_KEY,KIND,ITEM_ID,ITEM_CODE,ITEM_NAME,UNIT_CODE,SYSTEM_QTY,COUNTED_QTY,NOTES,STAMP,ITEM_STATE,NEEDS_RECOUNT)
            VALUES(@id,@ItemKey,@Kind,@ItemId,@Code,@Name,@Unit,@SystemQuantity,@CountedQuantity,@Notes,@Stamp,@ItemState,@NeedsRecount)
            """,new{id,line.ItemKey,line.Kind,line.ItemId,line.Code,line.Name,line.Unit,line.SystemQuantity,line.CountedQuantity,line.Notes,line.Stamp,line.ItemState,line.NeedsRecount},t,cancellationToken:ct));
    private static Task Touch(FbConnection c,FbTransaction t,HeaderRow h,AuthenticatedSession s,CancellationToken ct,string? notes=null)=>
        c.ExecuteAsync(new CommandDefinition("UPDATE INV_COUNTS SET REVISION=REVISION+1,NOTES=@notes,UPDATED_BY=@UserId,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID=@Id",
            new{h.Id,notes=notes??h.Notes,s.UserId},t,cancellationToken:ct));
    private static Task Audit(FbConnection c,FbTransaction t,long id,string type,AuthenticatedSession s,object payload,CancellationToken ct)=>
        AuditLogWriter.WriteAsync(c,t,type,s.UserId,s.Username,s.BranchId,"INV_COUNTS",id.ToString(),JsonSerializer.Serialize(payload),ct);
    private static async Task<List<StocktakeLine>> Snapshot(FbConnection c,FbTransaction t,LocationRow l,AuthenticatedSession s,CancellationToken ct)
    {
        var sql=l.Scope==InventoryScopes.RawMaterial?"""
            SELECT M.ID ItemId,M.CODE Code,M.NAME Name,M.UNIT_CODE Unit,COALESCE(B.QUANTITY,0) Quantity,
             COALESCE(B.AVERAGE_COST,0) Cost,B.UPDATED_AT Modified,COALESCE(B.COUNT_TOKEN,0) Version,CAST(@Id AS BIGINT) Location,'' State
            FROM MATERIALS M LEFT JOIN STOCK_BALANCES B ON B.MATERIAL_ID=M.ID AND B.LOCATION_ID=@Id
            WHERE M.COMPANY_ID=@Company AND (M.IS_ACTIVE=TRUE OR B.MATERIAL_ID IS NOT NULL) ORDER BY M.ID
            """:"""
            SELECT F.ID ItemId,F.CODE Code,F.PRODUCT_NAME Name,'món' Unit,CAST(1 AS DECIMAL(18,4)) Quantity,
             F.TOTAL_COST Cost,F.COUNT_TOKEN Version,COALESCE(F.LOCATION_ID,0) Location,F.STATUS_CODE State
            FROM FINISHED_PRODUCTS F WHERE F.COMPANY_ID=@Company AND F.BRANCH_ID=@BranchId AND F.LOCATION_ID=@Id
             AND F.STATUS_CODE IN ('IN_STOCK','DISPLAYED','RESERVED') ORDER BY F.ID
            """;
        var rows=await c.QueryAsync<SnapshotRow>(new CommandDefinition(sql,new{l.Id,l.Company,s.BranchId},t,cancellationToken:ct));
        return rows.Select(x=>ToLine(x,l.Scope==InventoryScopes.RawMaterial?"MATERIAL":"SERIAL")).ToList();
    }
    private static StocktakeLine ToLine(SnapshotRow row,string kind)=>new(0,(kind=="MATERIAL"?"M:":"F:")+row.ItemId,kind,row.ItemId,row.Code,row.Name,row.Unit,
        row.Quantity,kind=="MATERIAL"?null:0m,"",Hash(JsonSerializer.Serialize(row)),row.State);
    private static bool Available(string state)=>state is "IN_STOCK" or "DISPLAYED" or "RESERVED";
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Text(string? value,int max,string label){if(value is null || value.Length>max)throw new InvalidOperationException($"{label} tối đa {max} ký tự.");}
    private sealed class LocationRow {public long Id{get;set;} public long Company{get;set;} public string Scope{get;set;}="";public long Version{get;set;}}
    private sealed class HeaderRow
    {
        public long Id{get;set;} public long Company{get;set;} public string Code{get;set;}="";public DateTime CountDate{get;set;}
        public long LocationId{get;set;} public string LocationName{get;set;}="";public string Scope{get;set;}="";public string Status{get;set;}="";
        public int Revision{get;set;}public long InventoryRevision{get;set;}public string Notes{get;set;}="";public string CreatedByName{get;set;}="";
    }
    private sealed class SnapshotRow
    {
        public long ItemId{get;set;}public string Code{get;set;}="";public string Name{get;set;}="";public string Unit{get;set;}="";
        public decimal Quantity{get;set;}public decimal Cost{get;set;}public long Version{get;set;}public long Location{get;set;}
        public string State{get;set;}="";public DateTime? Modified{get;set;}
    }
}
