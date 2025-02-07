using System;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyUniqueThreeValue(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory)
    : TableKeyUniqueThreeValueCRUD(cache, scopedConnectionFactory), ITableMigrator
{
}