using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.RepositoryPattern.Entities;

namespace Odin.Core.Storage.RepositoryPattern.Repositories.System;

public interface IJobRepository
{
    Task<IEnumerable<Job>> GetAllAsync();
    Task<Job> GetByIdAsync(Guid id);
    Task<int> AddAsync(Job job);
    Task<int> UpdateAsync(Job job);
    Task<int> DeleteAsync(Guid id);

    Task<int> SpecializedQueryThatUsesNonPortableSql();

}