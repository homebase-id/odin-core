using System;
using System.Threading.Tasks;

namespace Odin.Core.Storage.RepositoryPattern.Repositories.System;

public class SqliteJobRepositoryStrategy(Connection.System.ISystemDbConnection connection) : IJobRepositoryStrategy
{
    public Task<int> SpecializedQueryThatUsesNonPortableSql()
    {
        throw new NotImplementedException();
    }
}