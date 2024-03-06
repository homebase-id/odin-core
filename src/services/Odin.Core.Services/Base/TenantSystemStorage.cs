using System;
using System.IO;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Util;

namespace Odin.Core.Services.Base
{
    public class TenantSystemStorage : IDisposable
    {
        public IdentityDatabase IdentityDatabase { get; }

        public TenantSystemStorage(TenantContext tenantContext)
        {
            ArgumentNullException.ThrowIfNull(tenantContext);
            ArgumentNullException.ThrowIfNull(tenantContext.StorageConfig);

            string dbPath = tenantContext.StorageConfig.HeaderDataStoragePath;
            if (!Directory.Exists(dbPath))
            {
                Directory.CreateDirectory(dbPath!);
            }

            string dbName = "identity.db";
            string finalPath = PathUtil.Combine(dbPath, dbName);

            if (!File.Exists(finalPath))
            {
                string oldName = "sys.db";
                finalPath = PathUtil.Combine(dbPath, oldName);
            }

            IdentityDatabase = new IdentityDatabase($"Data Source={finalPath}");
            IdentityDatabase.CreateDatabase(false);

            // TwoKeyValueStorage = new TwoKeyValueStorage(_db.tblKeyTwoValue);

            Connections = IdentityDatabase.tblConnections;
            CircleMemberStorage = IdentityDatabase.tblCircleMember;
            AppGrants = IdentityDatabase.tblAppGrants;

            Outbox = IdentityDatabase.tblOutbox;
            Inbox = IdentityDatabase.tblInbox;
            WhoIFollow = IdentityDatabase.tblImFollowing;
            Followers = IdentityDatabase.tblFollowsMe;
            Feedbox = IdentityDatabase.tblFeedDistributionOutbox;
            AppNotifications = IdentityDatabase.tblAppNotificationsTable;
        }

        public TableAppGrants AppGrants { get; }

        public TableConnections Connections { get; }

        public TableAppNotifications AppNotifications { get; }

        public TableFeedDistributionOutbox Feedbox { get; }

        public TableOutbox Outbox { get; }

        public TableInbox Inbox { get; }

        public TableImFollowing WhoIFollow { get; }

        public TableFollowsMe Followers { get; }

        public TableCircleMember CircleMemberStorage { get; }

        public DatabaseBase.LogicCommitUnit CreateCommitUnitOfWork()
        {
            return IdentityDatabase.CreateCommitUnitOfWork();
        }

        /// <summary>
        /// Store values using a single key
        /// </summary>
        public SingleKeyValueStorage CreateSingleKeyValueStorage(Guid contextKey)
        {
            return new SingleKeyValueStorage(IdentityDatabase.tblKeyValue, contextKey);
        }

        public TwoKeyValueStorage CreateTwoKeyValueStorage(Guid contextKey)
        {
            return new TwoKeyValueStorage(IdentityDatabase.tblKeyTwoValue, contextKey);
        }

        /// <summary>
        /// Store values using a single key while offering 2 other keys to categorize your data
        /// </summary>
        /// <param name="contextKey">Will be combined with the key to ensure unique storage in the TblKeyThreeValue table</param>
        public ThreeKeyValueStorage CreateThreeKeyValueStorage(Guid contextKey)
        {
            return new ThreeKeyValueStorage(IdentityDatabase.TblKeyThreeValue, contextKey);
        }

        public void Dispose()
        {
            IdentityDatabase.Dispose();
        }
    }
}