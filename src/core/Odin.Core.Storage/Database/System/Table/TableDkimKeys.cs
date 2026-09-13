using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Connection;

namespace Odin.Core.Storage.Database.System.Table;

#nullable enable

public class TableDkimKeys(ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableDkimKeysCRUD(scopedConnectionFactory)
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    //

    public async Task<List<DkimKeysRecord>> GetByDomainAsync(OdinId domain)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var getCommand = cn.CreateCommand();
        {
            getCommand.CommandText = "SELECT rowId,domain,selector,algorithm,publicKey,privateKey,created,modified FROM DkimKeys " +
                                     "WHERE domain = @domain ORDER BY selector;";
            getCommand.AddParameter("@domain", DbType.String, domain.DomainName);

            var result = new List<DkimKeysRecord>();
            await using var rdr = await getCommand.ExecuteReaderAsync(CommandBehavior.Default);
            while (await rdr.ReadAsync())
            {
                result.Add(ReadRecordFromReaderAll(rdr));
            }
            return result;
        }
    }

    public async Task<int> DeleteByDomainAsync(OdinId domain)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var deleteCommand = cn.CreateCommand();
        {
            deleteCommand.CommandText = "DELETE FROM DkimKeys WHERE domain = @domain;";
            deleteCommand.AddParameter("@domain", DbType.String, domain.DomainName);
            return await deleteCommand.ExecuteNonQueryAsync();
        }
    }
}
