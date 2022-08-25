using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Base
{
    public class SystemStorage : ISystemStorage
    {
        private readonly ILogger<SystemStorage> _logger;
        private readonly TenantContext _tenantContext;

        private readonly KeyValueDatabase _db;

        public SystemStorage(ILogger<SystemStorage> logger, TenantContext tenantContext)
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
            Outbox = new TableOutbox(_db);
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

        /// <summary>
        /// Store values using a single key
        /// </summary>
        public SingleKeyValueStorage SingleKeyValueStorage { get; }

        /// <summary>
        /// Store values using a single key while offering 2 other keys to categorize your data
        /// </summary>
        public ThreeKeyValueStorage ThreeKeyValueStorage { get; }

        public TableOutbox Outbox { get; }
    }
}