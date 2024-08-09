using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Odin.Core.Storage.RepositoryPattern.Connection.System;
using Odin.Core.Time;

namespace Odin.Core.Storage.RepositoryPattern.Repositories.System;

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

public class TableJobs(ISystemDbConnectionFactory connectionFactory) : TableJobsCRUD(connectionFactory)
{
    public async Task<long> GetCountAsync()
    {
        await using var cn = await ConnectionFactory.CreateAsync();
        var result = await cn.ExecuteScalarAsync("SELECT COUNT(*) FROM jobs;");
        return (long)(result ?? 0L);
    }

    //

    public async Task<bool> JobIdExists(Guid jobId)
    {
        await using var cn = await ConnectionFactory.CreateAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT 1 FROM jobs WHERE id = @id;";

        var idParam = cmd.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = jobId.ToByteArray();
        cmd.Parameters.Add(idParam);

        var result = await cmd.ExecuteScalarAsync();
        return (long)(result ?? 0L) != 0L;
    }

    //

    public async Task<long?> GetNextRunTime()
    {
        await using var cn = await ConnectionFactory.CreateAsync();
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

        var nextRun = await cmd.ExecuteScalarAsync();
        var result = (long?)nextRun;

        return result;
    }

    //

    public async Task<JobsRecord?> GetNextScheduledJob()
    {
        await using var cn = await ConnectionFactory.CreateAsync();
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
        await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default);
        if (await rdr.ReadAsync())
        {
            result = ReadRecordFromReaderAll(rdr);
        }

        return result;
    }

    //

    public async Task<JobsRecord?> GetJobByHash(string jobHash)
    {
        await using var cn = await ConnectionFactory.CreateAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT * FROM jobs WHERE jobHash = @jobHash;";

        var jobHashParam = cmd.CreateParameter();
        jobHashParam.ParameterName = "@jobHash";
        jobHashParam.Value = jobHash;
        cmd.Parameters.Add(jobHashParam);

        JobsRecord? result = null;
        await using var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default);
        if (await rdr.ReadAsync())
        {
            result = ReadRecordFromReaderAll(rdr);
        }

        return result;
    }

    //

    public async Task DeleteExpiredJobs()
    {
        await using var cn = await ConnectionFactory.CreateAsync();
        await using var trx = await cn.BeginTransactionAsync(); // just for illustration
        try
        {
            await using var cmd = cn.CreateCommand();
            cmd.Transaction = trx;

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
            await trx.CommitAsync();
        }
        catch (Exception e)
        {
            await trx.RollbackAsync();
            throw new Exception("oh no", e);
        }
    }

    //

    public async Task DeleteExpiredJobsUsingDapper()
    {
        await using var cn = await ConnectionFactory.CreateAsync();
        await using var trx = await cn.BeginTransactionAsync(); // not needed here, just for illustration
        try
        {
            const string sql =
                """
                DELETE FROM jobs 
                WHERE @now > expiresAt
                """;

            await cn.ExecuteAsync(sql, new { now = UnixTimeUtc.Now().milliseconds }, trx);

            await trx.CommitAsync();
        }
        catch (Exception e)
        {
            await trx.RollbackAsync();
            throw new Exception("oh no", e);
        }
    }

    //

    public async Task<int> InsertMany(JobsRecord[] jobs, bool commit)
    {
        var result = 0;

        await using var cn = await ConnectionFactory.CreateAsync();
        await using var trx = await cn.BeginTransactionAsync();

        foreach (var job in jobs)
        {
            result += await Insert(job, trx);
        }

        if (commit)
        {
            await trx.CommitAsync();
        }

        return result;
    }


}