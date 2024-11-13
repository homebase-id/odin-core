using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.System.Table;

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

public class TableJobs(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableJobsCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    //

    public async Task<long> GetCountAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM jobs;";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return count;
    }

    //

    public async Task<bool> JobIdExistsAsync(Guid jobId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM jobs WHERE id = @id;";

        var idParam = cmd.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = jobId.ToByteArray();
        cmd.Parameters.Add(idParam);

        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return count > 0L;
    }

    //

    public async Task<long?> GetNextRunTimeAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            SELECT nextRun
            FROM jobs
            WHERE state = @scheduled
            ORDER BY nextRun
            LIMIT 1;
            """;

        var scheduled = cmd.CreateParameter();
        scheduled.ParameterName = "@scheduled";
        scheduled.Value = (int)JobState.Scheduled;
        cmd.Parameters.Add(scheduled);

        var nextRun = (long?)await cmd.ExecuteScalarAsync();
        return nextRun;
    }

    //

    public async Task<JobsRecord?> GetNextScheduledJobAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            -- sqlite: According to chatgpt, "immediate" is needed to help the atomic update
            -- below. The Microsoft driver doesn't support this on the API level,
            -- so I have no idea if it's just being ignored...
            BEGIN IMMEDIATE TRANSACTION;
            
            -- Select the next scheduled job... 
            WITH NextJob AS (
                SELECT id
                FROM jobs
                WHERE nextRun <= @now AND state = @scheduled
                ORDER BY priority ASC, nextRun ASC
                LIMIT 1
                -- SEB:NOTE PSQL: add FOR UPDATE to lock the row
            )

            -- ...and update it to preflight, while making sure nobody beat us to it.
            UPDATE jobs
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
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (await rdr.ReadAsync())
        {
            result = ReadRecordFromReaderAll(rdr);
        }

        return result;
    }

    //

    public async Task<JobsRecord?> GetJobByHashAsync(string jobHash)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM jobs WHERE jobHash = @jobHash;";

        var jobHashParam = cmd.CreateParameter();
        jobHashParam.ParameterName = "@jobHash";
        jobHashParam.Value = jobHash;
        cmd.Parameters.Add(jobHashParam);

        JobsRecord? result = null;
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (await rdr.ReadAsync())
        {
            result = ReadRecordFromReaderAll(rdr);
        }

        return result;
    }

    //

    public async Task DeleteExpiredJobsAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            DELETE FROM jobs 
            WHERE @now > expiresAt
            """;
        
        var now = cmd.CreateParameter();
        now.ParameterName = "@now";
        now.Value = UnixTimeUtc.Now().milliseconds;
        cmd.Parameters.Add(now);
        
        await cmd.ExecuteNonQueryAsync();
    }

    //

}