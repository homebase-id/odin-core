using Odin.Core.Storage.Database.System.Connection;

namespace Odin.Core.Storage.Database.System.Table;

#nullable enable

public class TableSettings(ScopedSystemConnectionFactory scopedConnectionFactory)
    : TableSettingsCRUD(scopedConnectionFactory)
{
    private readonly ScopedSystemConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

}