using Odin.Core.Storage.Database.Identity.Connection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database;

public abstract class TableBase
{
    ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }

    public abstract Task EnsureTableExistsAsync(bool dropExisting);
    public static List<string> GetColumnNames() => throw new NotImplementedException();
    protected virtual Task<int> GetCountAsync() => throw new NotImplementedException();
}
