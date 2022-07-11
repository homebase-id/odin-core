using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Base
{
    public class LiteDbSystemStorage : ISystemStorage
    {
        private readonly ILogger<LiteDbSystemStorage> _logger;
        private readonly TenantContext _tenantContext;
        private readonly KeyValueStorage _keyValueStorage;

        public LiteDbSystemStorage(ILogger<LiteDbSystemStorage> logger, TenantContext tenantContext)
        {
            _logger = logger;
            _tenantContext = tenantContext;
            _keyValueStorage = new KeyValueStorage(tenantContext.StorageConfig.DataStoragePath, "sys.db");
        }

        public void WithTenantSystemStorage<T>(string collection, Action<IStorage<T>> action)
        {
            var cfg = _tenantContext.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                action(storage);
            }
        }

        public Task<PagedResult<T>> WithTenantSystemStorageReturnList<T>(string collection, Func<IStorage<T>, Task<PagedResult<T>>> func)
        {
            var cfg = _tenantContext.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                return func(storage);
            }
        }

        public Task<T> WithTenantSystemStorageReturnSingle<T>(string collection, Func<IStorage<T>, Task<T>> func)
        {
            var cfg = _tenantContext.StorageConfig;
            using (var storage = new LiteDBSingleCollectionStorage<T>(_logger, cfg.DataStoragePath, collection))
            {
                return func(storage);
            }
        }

        public KeyValueStorage KeyValueStorage
        {
            get
            {
                return this._keyValueStorage;
            }
        }
    }
}