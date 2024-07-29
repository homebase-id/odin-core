using System;
using System.Threading.Tasks;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.ServerDatabase;

#nullable enable

public enum JobState
{
    Unknown,
    Scheduled,
    Preflight,
    Running,
    Succeeded,
    Failed
};

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

    public Task<long?> GetNextRunTime(DatabaseConnection cn)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT nextRun
            FROM Jobs
            WHERE state = @scheduled
            ORDER BY nextRun
            LIMIT 1;
            """;

        var scheduled = cmd.CreateParameter();
        scheduled.ParameterName = "@scheduled";
        scheduled.Value = (int)JobState.Scheduled;
        cmd.Parameters.Add(scheduled);

        var nextRun = cn.ExecuteScalar(cmd);
        var result = (long?)nextRun;

        return Task.FromResult(result);
    }
    
    //
    
    public Task<JobsRecord?> GetNextScheduledJob(DatabaseConnection cn)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            -- sqlite: According to chatgpt, "immediate" is needed to help the atomic update
            -- below. The Microsoft driver doesn't support this on the API level,
            -- so I have no idea if it's just being ignored...
            BEGIN IMMEDIATE TRANSACTION;
            
            -- Select the next scheduled job... 
            WITH NextJob AS (
                SELECT id
                FROM Jobs
                WHERE nextRun <= @now AND state = @scheduled
                ORDER BY priority ASC, nextRun ASC
                LIMIT 1
                -- SEB:NOTE PSQL: add FOR UPDATE to lock the row
            )

            -- ...and update it to preflight, while making sure nobody beat us to it.
            UPDATE Jobs
            SET state = @preflight
            WHERE Id = (SELECT Id FROM NextJob) AND state = @scheduled
            RETURNING *;
            
            COMMIT;
            """;

        var now = cmd.CreateParameter();
        now.ParameterName = "@now";
        now.Value = UnixTimeUtc.Now().milliseconds;
        cmd.Parameters.Add(now);
        
        var scheduled = cmd.CreateParameter();
        scheduled.ParameterName = "@scheduled";
        scheduled.Value = (int)JobState.Scheduled;
        cmd.Parameters.Add(scheduled);
        
        var preflight = cmd.CreateParameter();
        preflight.ParameterName = "@preflight";
        preflight.Value = (int)JobState.Preflight;
        cmd.Parameters.Add(preflight);

        JobsRecord? result = null;
        var rdr = cn.ExecuteReader(cmd, System.Data.CommandBehavior.Default);
        if (rdr.Read())
        {
            result = ReadRecordFromReaderAll(rdr);
        }

        return Task.FromResult(result);
    }
    
    //

}