using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Odin.Core.Storage.RepositoryPattern.Connection.System;
using Odin.Core.Storage.RepositoryPattern.Entities;

namespace Odin.Core.Storage.RepositoryPattern.Repositories.System;

public class JobRepository(
    ISystemDbConnectionFactory connectionFactory,
    IJobRepositoryStrategy jobRepositoryStrategy) : IJobRepository
{
    public async Task<IEnumerable<Job>> GetAllAsync()
    {
        var cn = await connectionFactory.CreateAsync();
        const string sql = "SELECT * FROM Jobs";
        return await cn.QueryAsync<Job>(sql);
    }

    public async Task<Job> GetByIdAsync(Guid id)
    {
        var cn = await connectionFactory.CreateAsync();
        const string sql = "SELECT * FROM Jobs WHERE Id = @Id";
        return await cn.QuerySingleOrDefaultAsync<Job>(sql, new { Id = id });
    }

    public Task<int> AddAsync(Job job)
    {
        throw new NotImplementedException();
    }

    public Task<int> UpdateAsync(Job job)
    {
        throw new NotImplementedException();
    }

    public Task<int> DeleteAsync(Guid id)
    {
        throw new NotImplementedException();
    }

    public async Task<int> SpecializedQueryThatUsesNonPortableSql()
    {
        return await jobRepositoryStrategy.SpecializedQueryThatUsesNonPortableSql();
    }
}