using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database;

public abstract class TableBase
{
    public abstract string TableName { get; }
    public static List<string> GetColumnNames() => throw new NotImplementedException();
    protected virtual Task<int> GetCountAsync() => throw new NotImplementedException();
}
