using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableKeyUniqueThreeValueCache(
    IGenericMemoryCache<TableKeyUniqueThreeValueCache> cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
#pragma warning disable CS9113 // Parameter is unread.
    TableKeyUniqueThreeValue table)
#pragma warning restore CS9113 // Parameter is unread.
    : AbstractTableCache(cache, scopedConnectionFactory)
{
}