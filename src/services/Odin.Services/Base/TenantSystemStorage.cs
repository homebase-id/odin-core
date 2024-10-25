using System;
using System.IO;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Util;

namespace Odin.Services.Base;

public sealed class TenantSystemStorage : IDisposable
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

        var dbName = "identity.db";
        var finalPath = PathUtil.Combine(dbPath, dbName);

        IdentityDatabase = new IdentityDatabase(tenantContext.DotYouRegistryId, finalPath);
        IdentityDatabase.CreateDatabaseAsync(false).Wait(); // SEB:TODO move out of ctor and make async

        Connections = IdentityDatabase.tblConnections;
        CircleMemberStorage = IdentityDatabase.tblCircleMember;
        AppGrants = IdentityDatabase.tblAppGrants;
        Outbox = IdentityDatabase.tblOutbox;
        Inbox = IdentityDatabase.tblInbox;
        WhoIFollow = IdentityDatabase.tblImFollowing;
        Followers = IdentityDatabase.tblFollowsMe;
        AppNotifications = IdentityDatabase.tblAppNotificationsTable;
    }

    public void Dispose()
    {
        IdentityDatabase.Dispose();
    }

    public SingleKeyValueStorage CreateSingleKeyValueStorage(Guid contextKey)
    {
        return new SingleKeyValueStorage(contextKey);
    }

    public TwoKeyValueStorage CreateTwoKeyValueStorage(Guid contextKey)
    {
        return new TwoKeyValueStorage(contextKey);
    }

    /// <summary>
    /// Store values using a single key while offering 2 other keys to categorize your data
    /// </summary>
    /// <param name="contextKey">Will be combined with the key to ensure unique storage in the TblKeyThreeValue table</param>
    public ThreeKeyValueStorage CreateThreeKeyValueStorage(Guid contextKey)
    {
        return new ThreeKeyValueStorage(contextKey);
    }

    public TableAppGrants AppGrants { get; }
    public TableConnections Connections { get; }
    public TableAppNotifications AppNotifications { get; }
    public TableOutbox Outbox { get; }
    public TableInbox Inbox { get; }
    public TableImFollowing WhoIFollow { get; }
    public TableFollowsMe Followers { get; }
    public TableCircleMember CircleMemberStorage { get; }
}
