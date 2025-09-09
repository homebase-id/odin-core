using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.System.Table;

#nullable enable

public class TableLastSeen(ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableLastSeenCRUD(scopedConnectionFactory)
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    //

    public async Task<List<LastSeenRecord>> GetAllAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM LastSeen;";

        var result = new List<LastSeenRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();

        while (await rdr.ReadAsync())
        {
            result.Add(ReadRecordFromReaderAll(rdr));
        }

        return result;
    }

    //

    public async Task UpdateLastSeenAsync(Dictionary<string, UnixTimeUtc> lastSeenBySubject)
    {
        if (lastSeenBySubject.Count == 0)
        {
            return;
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        var sb = new StringBuilder();
        var idx = 0;
        foreach (var record in lastSeenBySubject)
        {
            var timestampParam = $"@timestamp{idx}";
            var subjectParam = $"@subject{idx}";

            sb.AppendLine(
                $"""
                INSERT INTO LastSeen (subject, timestamp)
                VALUES ({subjectParam}, {timestampParam})
                ON CONFLICT(subject) DO UPDATE
                SET timestamp = {timestampParam}
                WHERE LastSeen.timestamp IS NULL OR LastSeen.timestamp < EXCLUDED.timestamp;
                """);

            cmd.AddParameter(timestampParam, DbType.Int64, record.Value.milliseconds);
            cmd.AddParameter(subjectParam, DbType.String, record.Key);

            idx++;
        }

        cmd.CommandText = sb.ToString();
        if (cmd.CommandText.Length > 0)
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    //

    public async Task<UnixTimeUtc?> GetLastSeenAsync(string subject)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT timestamp FROM LastSeen where subject = @subject;";
        cmd.AddParameter("@subject", DbType.String, subject);

        var rs = await cmd.ExecuteScalarAsync();
        if (rs == DBNull.Value || rs == null)
        {
            return null;
        }
        return new UnixTimeUtc((long)rs);
    }

    //

    public async Task DeleteOldRecordsAsync(TimeSpan deleteOlderThan)
    {
        var threshold = DateTimeOffset.UtcNow.Add(-deleteOlderThan).ToUnixTimeMilliseconds();

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM LastSeen WHERE timestamp < @threshold;";
        cmd.AddParameter("@threshold", DbType.Int64, threshold);

        await cmd.ExecuteNonQueryAsync();
    }

    //

}