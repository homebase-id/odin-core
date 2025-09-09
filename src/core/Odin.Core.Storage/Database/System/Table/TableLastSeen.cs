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

    public async Task UpdateLastSeenAsync(Dictionary<Guid, UnixTimeUtc> lastSeenByIdentityId)
    {
        if (lastSeenByIdentityId.Count == 0)
        {
            return;
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        var sb = new StringBuilder();
        var idx = 0;
        foreach (var record in lastSeenByIdentityId)
        {
            var timestampParam = $"@timestamp{idx}";
            var identityIdParam = $"@identityId{idx}";

            sb.AppendLine(
                $"""
                INSERT INTO LastSeen (identityId, timestamp)
                VALUES ({identityIdParam}, {timestampParam})
                ON CONFLICT(identityId) DO UPDATE
                SET timestamp = {timestampParam}
                WHERE LastSeen.timestamp IS NULL OR LastSeen.timestamp < EXCLUDED.timestamp;
                """);

            cmd.AddParameter(timestampParam, DbType.Int64, record.Value.milliseconds);
            cmd.AddParameter(identityIdParam, DbType.Binary, record.Key.ToByteArray());

            idx++;
        }

        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync();
    }

    //

    public async Task<UnixTimeUtc?> GetLastSeenAsync(Guid identityId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT timestamp FROM LastSeen where identityId = @identityId;";
        cmd.AddParameter("@identityId", DbType.Binary, identityId.ToByteArray());

        var rs = await cmd.ExecuteScalarAsync();
        if (rs == DBNull.Value || rs == null)
        {
            return null;
        }
        return new UnixTimeUtc((long)rs);
    }

    //

}