using Odin.Core.Storage.Database.System.Connection;

namespace Odin.Core.Storage.Database.System;

#nullable enable

public class TableSettings(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableSettingsCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

}