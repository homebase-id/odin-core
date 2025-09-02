using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.System.Table;

#nullable enable

//

public record LastSeenEntry(Guid IdentityId, string Domain, UnixTimeUtc LastSeen);

//

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

    public async Task UpdateLastSeenAsync(Dictionary<Guid, LastSeenEntry> lastSeenByIdentityId)
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
            var lastSeenParam = $"@lastSeen{idx}";
            var identityIdParam = $"@identityId{idx}";

            sb.AppendLine(
                $"""
                UPDATE Registrations SET lastSeen = {lastSeenParam} 
                WHERE identityId = {identityIdParam}
                AND (lastSeen IS NULL OR lastSeen < {lastSeenParam});
                """);

            cmd.AddParameter(lastSeenParam, DbType.Int64, record.Value.LastSeen.milliseconds);
            cmd.AddParameter(identityIdParam, DbType.Binary, record.Key.ToByteArray());

            idx++;
        }

        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync();
    }

    //

    public async Task<LastSeenEntry?> GetLastSeenAsync(Guid identityId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT primaryDomainName, lastSeen FROM Registrations where identityId = @identityId;";
        cmd.AddParameter("@identityId", DbType.Binary, identityId.ToByteArray());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        if (reader[0] == DBNull.Value || reader[1] == DBNull.Value)
        {
            return null;
        }

        var domain = (string)reader[0];
        var timestamp = new UnixTimeUtc((long)reader[1]);

        return new LastSeenEntry(
            identityId,
            domain,
            timestamp);
    }

    //

    public async Task<LastSeenEntry?> GetLastSeenAsync(string domain)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT identityId, lastSeen FROM Registrations where primaryDomainName = @domain;";
        cmd.AddParameter("@domain", DbType.String, domain);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        if (reader[0] == DBNull.Value || reader[1] == DBNull.Value)
        {
            return null;
        }

        var identityId = new Guid((byte[])reader[0]);
        var timestamp = new UnixTimeUtc((long)reader[1]);

        return new LastSeenEntry(
            identityId,
            domain,
            timestamp);
    }

    //

}


