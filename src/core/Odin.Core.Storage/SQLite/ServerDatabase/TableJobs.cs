using System;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite.ServerDatabase;

#nullable enable

public class TableJobs : TableJobsCRUD
{
    private readonly ServerDatabase _db;

    public TableJobs(ServerDatabase db, CacheHelper? cache) : base(db, cache)
    {
        _db = db;
    }
    
    //

    public Task<long> GetCountAsync(DatabaseConnection cn)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM jobs;";
        var result = (long)(cn.ExecuteScalar(cmd) ?? 0L); 
        return Task.FromResult(result); 
    }
    
    //
    
    public Task<bool> JobIdExists(DatabaseConnection cn, Guid jobId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM jobs WHERE id = @id;";
        
        var idParam = cmd.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = jobId.ToByteArray();
        cmd.Parameters.Add(idParam);
        
        var result = (long)(cn.ExecuteScalar(cmd) ?? 0L) != 0L;
        return Task.FromResult(result);
    }
    
    //
    
    
    
}