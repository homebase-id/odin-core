using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Connection;
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

public class TableJobs(ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableJobsCRUD(scopedConnectionFactory)
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    //

    public new async Task<long> GetCountAsync()
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
        cmd.CommandText = "SELECT COUNT(id) FROM jobs WHERE id = @id;";

        var idParam = cmd.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = jobId.ToByteArray();
        cmd.Parameters.Add(idParam);

        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return count > 0L;
    }

    //

    public async Task<int> DeleteByHashAsync(string jobHash)
    {
        if (string.IsNullOrEmpty(jobHash))
        {
            return 0;
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM Jobs WHERE jobHash = @hash";
        cmd.AddParameter("@hash", DbType.String, jobHash);
        var count = await cmd.ExecuteNonQueryAsync();
        return count;
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
        cmd.CommandText = _scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite
            ?
            //
            // Sqlite specific sql
            //
            """
            BEGIN IMMEDIATE TRANSACTION;

            -- Select the next scheduled job... 
            WITH NextJob AS (
                SELECT id
                FROM jobs
                WHERE nextRun <= @now AND state = @scheduled
                ORDER BY priority ASC, nextRun ASC
                LIMIT 1
            )

            -- ...and update it to preflight, while making sure nobody beat us to it.
            -- We bump modified here because this raw UPDATE bypasses the CRUD audit hook;
            -- LogOrphanedJobsAsync uses modified to detect stuck rows.
            UPDATE jobs
            SET state = @preflight, modified = @now
            WHERE Id = (SELECT Id FROM NextJob) AND state = @scheduled
            RETURNING *;

            COMMIT;
            """
            :
            //
            // Postgres specific sql
            //
            """
            -- Select the next scheduled job... 
            WITH NextJob AS (
                SELECT id
                FROM jobs
                WHERE nextRun <= @now AND state = @scheduled
                ORDER BY priority ASC, nextRun ASC
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            ),
            -- ...and update it to preflight, while making sure nobody beat us to it.
            -- We bump modified here because this raw UPDATE bypasses the CRUD audit hook;
            -- LogOrphanedJobsAsync uses modified to detect stuck rows.
            updated as (
                UPDATE jobs
                SET state = @preflight, modified = @now
                WHERE Id = (SELECT Id FROM NextJob) AND state = @scheduled
                RETURNING *
            )
            SELECT * FROM updated
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

    public async Task<List<JobsRecord>> GetAllJobsAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM jobs ORDER BY nextRun;";

        var result = new List<JobsRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            result.Add(ReadRecordFromReaderAll(rdr));
        }

        return result;
    }

    //

    // Returns jobs stuck mid-flight: in Preflight longer than the preflight cutoff, or in Running
    // longer than the running cutoff (cutoffs are absolute millisecond timestamps). Used by
    // JobManager.LogOrphanedJobsAsync.
    public async Task<List<JobsRecord>> GetOrphanedJobsAsync(long preflightCutoffMs, long runningCutoffMs)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            SELECT * FROM jobs
            WHERE (state = @preflight AND modified <= @preflightCutoff)
               OR (state = @running   AND modified <= @runningCutoff)
            """;

        var preflight = cmd.CreateParameter();
        preflight.ParameterName = "@preflight";
        preflight.Value = (int)JobState.Preflight;
        cmd.Parameters.Add(preflight);

        var running = cmd.CreateParameter();
        running.ParameterName = "@running";
        running.Value = (int)JobState.Running;
        cmd.Parameters.Add(running);

        var preflightCutoff = cmd.CreateParameter();
        preflightCutoff.ParameterName = "@preflightCutoff";
        preflightCutoff.Value = preflightCutoffMs;
        cmd.Parameters.Add(preflightCutoff);

        var runningCutoff = cmd.CreateParameter();
        runningCutoff.ParameterName = "@runningCutoff";
        runningCutoff.Value = runningCutoffMs;
        cmd.Parameters.Add(runningCutoff);

        var result = new List<JobsRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            result.Add(ReadRecordFromReaderAll(rdr));
        }

        return result;
    }

    //

    public async Task<List<JobsRecord>> GetJobsByIdentityIdAsync(Guid identityId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM jobs WHERE identityId = @identityId ORDER BY nextRun;";
        cmd.AddParameter("@identityId", DbType.Binary, identityId);

        var result = new List<JobsRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            result.Add(ReadRecordFromReaderAll(rdr));
        }

        return result;
    }

    //

    public async Task<int> DeleteJobsByIdentityIdAsync(Guid identityId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM jobs WHERE identityId = @identityId;";
        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        var count = await cmd.ExecuteNonQueryAsync();
        return count;
    }

    //

    // Deletes a single job by id, but only if it belongs to the given identity. Used to let a
    // tenant cancel its own jobs without being able to delete another tenant's job by guessing its id.
    public async Task<int> DeleteAsync(Guid id, Guid identityId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM jobs WHERE id = @id AND identityId = @identityId;";
        cmd.AddParameter("@id", DbType.Binary, id);
        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        var count = await cmd.ExecuteNonQueryAsync();
        return count;
    }

    //

    // Replaces a job's data and due time in place (same id, same identityId ownership check as
    // DeleteAsync above), resetting it to a fresh Scheduled state so it runs again regardless of
    // whatever state it was previously in (including a terminal Succeeded/Failed).
    public async Task<int> UpdateAsync(Guid id, Guid identityId, string jobData, long nextRun)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE jobs
            SET jobData = @jobData, nextRun = @nextRun, state = @scheduled, runCount = 0, lastError = NULL, expiresAt = NULL, modified = @modified
            WHERE id = @id AND identityId = @identityId;
            """;
        cmd.AddParameter("@jobData", DbType.String, jobData);
        cmd.AddParameter("@nextRun", DbType.Int64, nextRun);
        cmd.AddParameter("@scheduled", DbType.Int32, (int)JobState.Scheduled);
        cmd.AddParameter("@modified", DbType.Int64, UnixTimeUtc.Now().milliseconds);
        cmd.AddParameter("@id", DbType.Binary, id);
        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        var count = await cmd.ExecuteNonQueryAsync();
        return count;
    }

    //

}