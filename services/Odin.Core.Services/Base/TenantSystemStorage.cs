using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Util;

namespace Odin.Core.Services.Base
{
    public class TenantSystemStorage : IDisposable
    {
        private readonly ILogger<TenantSystemStorage> _logger;

        private readonly IdentityDatabase _db;

        public TenantSystemStorage(ILogger<TenantSystemStorage> logger, TenantContext tenantContext)
        {
            ArgumentNullException.ThrowIfNull(tenantContext);
            ArgumentNullException.ThrowIfNull(tenantContext.StorageConfig);
                
            _logger = logger;

            string dbPath = tenantContext.StorageConfig.HeaderDataStoragePath;
            string dbName = "sys.db";
            if (!Directory.Exists(dbPath))
            {
                Directory.CreateDirectory(dbPath!);
            }

            string finalPath = PathUtil.Combine(dbPath, $"{dbName}");
            _db = new IdentityDatabase($"Data Source={finalPath}");
            _db.CreateDatabase(false);

            SingleKeyValueStorage = new SingleKeyValueStorage(_db.tblKeyValue);
            ThreeKeyValueStorage = new ThreeKeyValueStorage(_db.TblKeyThreeValue);
            TwoKeyValueStorage = new TwoKeyValueStorage(_db.tblKeyTwoValue);

            Connections = _db.tblConnections;
            CircleMemberStorage = _db.tblCircleMember;
            AppGrants = _db.tblAppGrants;

            Outbox = _db.tblOutbox;
            Inbox = _db.tblInbox;
            WhoIFollow = _db.tblImFollowing;
            Followers = _db.tblFollowsMe;
            Feedbox = _db.tblFeedDistributionOutbox;
        }

        public TableAppGrants AppGrants { get; }

        public TableConnections Connections { get; }

        /// <summary>
        /// Store values using a single key
        /// </summary>
        public SingleKeyValueStorage SingleKeyValueStorage { get; }

        /// <summary>
        /// Store values using a single key while offering 2 other keys to categorize your data
        /// </summary>
        public ThreeKeyValueStorage ThreeKeyValueStorage { get; }

        public TwoKeyValueStorage TwoKeyValueStorage { get; }
        
        public TableFeedDistributionOutbox Feedbox { get; }

        public TableOutbox Outbox { get; }

        public TableInbox Inbox { get; }

        public TableImFollowing WhoIFollow { get; }

        public TableFollowsMe Followers { get; }
        
        public TableCircleMember CircleMemberStorage { get; }

        public DatabaseBase.LogicCommitUnit CreateCommitUnitOfWork()
        {
            return _db.CreateCommitUnitOfWork();
        }

        public void Dispose()
        {
            _db.Dispose();
        }
    }
}