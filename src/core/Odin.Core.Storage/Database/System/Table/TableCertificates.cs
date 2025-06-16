using Odin.Core.Storage.Database.System.Connection;

namespace Odin.Core.Storage.Database.System.Table;

#nullable enable

public class TableCertificates(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableCertificatesCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

}