using Kazuko.Core.Production;

namespace Kazuko.Infrastructure.Production;

// Database rollback cannot undo filesystem IO. Stage copies, remove old files only after commit.
internal sealed class ProductionAttachmentCommit(IProductionAttachmentStore store) : IDisposable
{
    private readonly List<string> _created=[],_staging=[],_deleteAfter=[];
    private bool _committed,_commitStarted;
    public async Task<string> FinalizeAsync(string staging,string code,CancellationToken token)
    {
        var working=Path.Combine(store.ManagedRoot,"_staging",Guid.NewGuid().ToString("N")+Path.GetExtension(staging));
        Directory.CreateDirectory(Path.GetDirectoryName(working)!);
        File.Copy(staging,working);
        try{var path=await store.FinalizeAsync(working,code,token);_created.Add(path);_staging.Add(staging);return path;}
        finally{store.TryDeleteStagingFile(working);}
    }
    public void DeleteAfterCommit(string? path){if(!string.IsNullOrWhiteSpace(path))_deleteAfter.Add(path);}
    public void BeginCommit()=>_commitStarted=true;
    public void Commit(){_committed=true;foreach(var path in _deleteAfter)store.TryDeleteManagedFile(path);foreach(var path in _staging)store.TryDeleteStagingFile(path);}
    public void Dispose(){if(!_committed&&!_commitStarted)foreach(var path in _created)store.TryDeleteManagedFile(path);}
}
