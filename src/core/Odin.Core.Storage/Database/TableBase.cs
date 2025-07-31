using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database;

public abstract class TableBase
{
    public abstract Task EnsureTableExistsAsync(bool dropExisting);
    public static List<string> GetColumnNames() => throw new NotImplementedException(); // SEB:TODO make abstract
    protected virtual Task<int> GetCountAsync() => throw new NotImplementedException(); // SEB:TODO make abstract
}
