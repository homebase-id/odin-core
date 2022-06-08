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

    public interface IKeyValueStorage
    {
        void Save<T>(string dbName, T value) where T :IKeyValueStorable;
    }

    public class SqliteKeyValueStore : IKeyValueStorage
    {
        public void Save<T>(string dbName, T value) where T : IKeyValueStorable
        {
            
            
        }
    }

    public interface IKeyValueStorable
    {
        byte[] GetPrimaryKey();
        byte[] SecondaryKey();
        byte[] GetTertiaryKey();
    }
}