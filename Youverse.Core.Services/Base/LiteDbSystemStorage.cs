using System;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.SystemStorage;
using Youverse.Core.SystemStorage.SqliteKeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Base
{
    public class LiteDbSystemStorage : ISystemStorage
    {
        private readonly ILogger<LiteDbSystemStorage> _logger;
        private readonly TenantContext _tenantContext;

        private readonly KeyValueDatabase _db;

        public LiteDbSystemStorage(ILogger<LiteDbSystemStorage> logger, TenantContext tenantContext)
        {
            _logger = logger;
            _tenantContext = tenantContext;

            string dbPath = tenantContext.StorageConfig.DataStoragePath;
            string dbName = "sys.db";
            if (!Directory.Exists(dbPath))
            {
                Directory.CreateDirectory(dbPath!);
            }

            string finalPath = PathUtil.Combine(dbPath, $"{dbName}.db");
            _db = new KeyValueDatabase($"URI=file:{finalPath}");
            _db.CreateDatabase(false);

            SingleKeyValueStorage = new SingleKeyValueStorage(_db.tblKeyValue);
            ThreeKeyValueStorage = new ThreeKeyValueStorage(_db.TblKeyThreeValue);
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

//        public SingleKeyValueStorage SingleKeyValueStorage { get; }
        
        public ThreeKeyValueStorage ThreeKeyValueStorage { get; }

        public SingleKeyValueStorage SingleKeyValueStorage { get; }
    }
}