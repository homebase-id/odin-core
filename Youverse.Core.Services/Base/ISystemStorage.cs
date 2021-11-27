using System;
using System.Threading.Tasks;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Storage of system specific data.
    /// </summary>
    public interface ISystemStorage
    {
        void WithTenantSystemStorage<T>(string collection, Action<IStorage<T>> action);

        Task<PagedResult<T>> WithTenantSystemStorageReturnList<T>(string collection, Func<IStorage<T>, Task<PagedResult<T>>> func);

        Task<T> WithTenantSystemStorageReturnSingle<T>(string collection, Func<IStorage<T>, Task<T>> func);
    }
}