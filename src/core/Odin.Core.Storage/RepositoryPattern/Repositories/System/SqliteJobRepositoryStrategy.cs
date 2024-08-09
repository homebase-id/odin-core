using System;
using System.Threading.Tasks;

namespace Odin.Core.Storage.RepositoryPattern.Repositories.System;

public class SqliteJobRepositoryStrategy(Connection.System.ISystemDbConnectionFactory connectionFactory) : IJobRepositoryStrategy
{
    public async Task<int> SpecializedQueryThatUsesNonPortableSql()
    {
        await using var cn = await connectionFactory.CreateAsync();

        throw new NotImplementedException();
    }
}