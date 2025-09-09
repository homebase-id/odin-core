using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.System.Table;

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

}


