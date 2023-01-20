using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Base
{
    public class TenantSystemStorage : ITenantSystemStorage
    {
        private readonly ILogger<TenantSystemStorage> _logger;
        private readonly TenantContext _tenantContext;

        private readonly IdentityDatabase _db;

        public TenantSystemStorage(ILogger<TenantSystemStorage> logger, TenantContext tenantContext)
        {
            _logger = logger;
            _tenantContext = tenantContext;

            string dbPath = tenantContext.StorageConfig.DataStoragePath;
            string dbName = "sys.db";
            if (!Directory.Exists(dbPath))
            {
                Directory.CreateDirectory(dbPath!);
            }

            string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
            _db = new IdentityDatabase($"URI=file:{finalPath}");
            _db.CreateDatabase(false);

            // TODO: NOT ALLOWED. THIS WILL MESS UP SOMEHOW. MS.

            SingleKeyValueStorage = new SingleKeyValueStorage(_db.tblKeyValue);
            ThreeKeyValueStorage = new ThreeKeyValueStorage(_db.TblKeyThreeValue);

            Outbox = _db.tblOutbox;
            Inbox = _db.tblInbox;

            IcrClientStorage = new ThreeKeyValueStorage(_db.TblKeyThreeValue);
            CircleMemberStorage = _db.tblCircleMember;
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

        public TableInbox Inbox { get; }

        public ThreeKeyValueStorage IcrClientStorage { get; }

        public TableCircleMember CircleMemberStorage { get; }

        public void Dispose()
        {
            _db.Dispose();
        }
    }
}