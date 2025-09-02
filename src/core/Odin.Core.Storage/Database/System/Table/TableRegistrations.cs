using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.System.Table;

#nullable enable

public class TableRegistrations(ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableRegistrationsCRUD(scopedConnectionFactory)
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<List<RegistrationsRecord>> GetAllAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Registrations;";

        var result = new List<RegistrationsRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();

        while (await rdr.ReadAsync())
        {
            result.Add(ReadRecordFromReaderAll(rdr));
        }

        return result;
    }

    //

    public async Task UpdateLastSeen(Dictionary<Guid, UnixTimeUtc> lastSeenByIdentityId)
    {
        if (lastSeenByIdentityId.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        foreach (var record in lastSeenByIdentityId)
        {
            var identityId = record.Key.ToByteArray().ToSql(_scopedConnectionFactory.DatabaseType);
            var lastSeen = record.Value.milliseconds;
            sb.AppendLine(
                $"""
                UPDATE Registrations SET lastSeen = {lastSeen} 
                WHERE identityId = {identityId}
                AND (lastSeen IS NULL OR lastSeen < {lastSeen});
                """);
        }

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync();
    }

    //

}


