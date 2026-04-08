using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.DatabaseImport;

// Seeds at least one row into every table on IdentityDatabase and SystemDatabase.
//
// Records use the simplest values that satisfy the table's Validate() method — we are
// not testing column round-tripping (the per-table CRUD tests already cover that),
// only that DataImporter actually copies rows from source to target.
//
// Identity tables auto-fill the identityId field on their InsertAsync override
// (from the DI-injected OdinIdentity), so seed records do not set identityId.
//
// When a developer adds a new table, the DataImporterEndToEndTests fixture's coverage
// guard fires (count == 0 after seeding) until a seeder is added here.
internal static class DataImporterSeedHelper
{
    // 16 zero bytes; satisfies the min-length-16 constraint on KeyValue keys.
    private static byte[] FreshKey() => Guid.NewGuid().ToByteArray();

    public static async Task SeedAllIdentityTablesAsync(IdentityDatabase db)
    {
        await SeedDrivesAsync(db);
        await SeedDriveMainIndexAsync(db);
        await SeedDriveTransferHistoryAsync(db);
        await SeedDriveAclIndexAsync(db);
        await SeedDriveTagIndexAsync(db);
        await SeedDriveLocalTagIndexAsync(db);
        await SeedDriveReactionsAsync(db);
        await SeedAppNotificationsAsync(db);
        await SeedClientRegistrationsAsync(db);
        await SeedCircleAsync(db);
        await SeedCircleMemberAsync(db);
        await SeedConnectionsAsync(db);
        await SeedAppGrantsAsync(db);
        await SeedImFollowingAsync(db);
        await SeedFollowsMeAsync(db);
        await SeedInboxAsync(db);
        await SeedOutboxAsync(db);
        await SeedKeyValueAsync(db);
        await SeedKeyTwoValueAsync(db);
        await SeedKeyThreeValueAsync(db);
        await SeedKeyUniqueThreeValueAsync(db);
        await SeedNonceAsync(db);
    }

    public static async Task SeedAllSystemTablesAsync(
        SystemDatabase db,
        string identityDomain,
        Guid identityId)
    {
        await SeedJobsAsync(db);
        await SeedCertificatesAsync(db, identityDomain);
        await SeedLastSeenAsync(db);
        await SeedRegistrationsAsync(db, identityDomain, identityId);
        await SeedSettingsAsync(db);
    }

    //
    // Identity-database seeders
    //

    private static async Task SeedDrivesAsync(IdentityDatabase db)
    {
        // Validate() only checks string non-null + length bounds; no real crypto needed.
        await db.Drives.InsertAsync(new DrivesRecord
        {
            DriveId = Guid.NewGuid(),
            StorageKeyCheckValue = Guid.NewGuid(),
            DriveType = Guid.NewGuid(),
            DriveName = "Seed Drive",
            MasterKeyEncryptedStorageKeyJson = "{}",
            EncryptedIdIv64 = "AAAAAAAAAAAAAAAAAAAAAA==",
            EncryptedIdValue64 = "AAAAAAAAAAAAAAAAAAAAAA==",
            detailsJson = "{}",
        });
    }

    private static async Task SeedDriveMainIndexAsync(IdentityDatabase db)
    {
        await db.DriveMainIndex.InsertAsync(new DriveMainIndexRecord
        {
            driveId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            globalTransitId = Guid.NewGuid(),
            uniqueId = Guid.NewGuid(),
            groupId = Guid.NewGuid(),
            fileType = 1,
            dataType = 1,
            archivalStatus = 0,
            historyStatus = 0,
            requiredSecurityGroup = 1,
            hdrEncryptedKeyHeader = "0000000000000000",
            hdrVersionTag = Guid.NewGuid(),
            hdrAppData = "{}",
            hdrServerData = "{}",
            hdrFileMetaData = "{}",
            hdrTmpDriveAlias = Guid.NewGuid(),
            hdrTmpDriveType = Guid.NewGuid(),
        });
    }

    private static async Task SeedDriveTransferHistoryAsync(IdentityDatabase db)
    {
        await db.DriveTransferHistory.InsertAsync(new DriveTransferHistoryRecord
        {
            driveId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            remoteIdentityId = new OdinId("seed-recipient.example.com"),
            latestTransferStatus = 0,
            isInOutbox = false,
            isReadByRecipient = new UnixTimeUtc(0),
            latestSuccessfullyDeliveredVersionTag = null,
        });
    }

    private static async Task SeedDriveAclIndexAsync(IdentityDatabase db)
    {
        await db.DriveAclIndex.InsertAsync(new DriveAclIndexRecord
        {
            driveId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            aclMemberId = Guid.NewGuid(),
        });
    }

    private static async Task SeedDriveTagIndexAsync(IdentityDatabase db)
    {
        await db.DriveTagIndex.InsertAsync(new DriveTagIndexRecord
        {
            driveId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            tagId = Guid.NewGuid(),
        });
    }

    private static async Task SeedDriveLocalTagIndexAsync(IdentityDatabase db)
    {
        await db.DriveLocalTagIndex.InsertAsync(new DriveLocalTagIndexRecord
        {
            driveId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            tagId = Guid.NewGuid(),
        });
    }

    private static async Task SeedDriveReactionsAsync(IdentityDatabase db)
    {
        await db.DriveReactions.InsertAsync(new DriveReactionsRecord
        {
            driveId = Guid.NewGuid(),
            postId = Guid.NewGuid(),
            identity = new OdinId("seed-reactor.example.com"),
            singleReaction = ":lol:",
        });
    }

    private static async Task SeedAppNotificationsAsync(IdentityDatabase db)
    {
        await db.AppNotifications.InsertAsync(new AppNotificationsRecord
        {
            notificationId = Guid.NewGuid(),
            senderId = "seed-sender.example.com",
            unread = 1,
            data = null,
        });
    }

    private static async Task SeedClientRegistrationsAsync(IdentityDatabase db)
    {
        await db.ClientRegistrations.InsertAsync(new ClientRegistrationsRecord
        {
            catId = Guid.NewGuid(),
            issuedToId = "seed-client",
            ttl = 3600,
            expiresAt = UnixTimeUtc.Now().AddSeconds(3600),
            categoryId = Guid.NewGuid(),
            catType = 1,
            value = null,
        });
    }

    private static async Task SeedCircleAsync(IdentityDatabase db)
    {
        await db.Circle.InsertAsync(new CircleRecord
        {
            circleId = Guid.NewGuid(),
            circleName = "Seed Circle",
            data = null,
        });
    }

    private static async Task SeedCircleMemberAsync(IdentityDatabase db)
    {
        await db.CircleMember.InsertAsync(new CircleMemberRecord
        {
            circleId = Guid.NewGuid(),
            memberId = Guid.NewGuid(),
            data = null,
        });
    }

    private static async Task SeedConnectionsAsync(IdentityDatabase db)
    {
        await db.Connections.InsertAsync(new ConnectionsRecord
        {
            identity = new OdinId("seed-connection.example.com"),
            displayName = "Seed Connection",
            status = 0,
            accessIsRevoked = 0,
            data = null,
        });
    }

    private static async Task SeedAppGrantsAsync(IdentityDatabase db)
    {
        await db.AppGrants.InsertAsync(new AppGrantsRecord
        {
            odinHashId = Guid.NewGuid(),
            appId = Guid.NewGuid(),
            circleId = Guid.NewGuid(),
            data = null,
        });
    }

    private static async Task SeedImFollowingAsync(IdentityDatabase db)
    {
        await db.ImFollowing.InsertAsync(new ImFollowingRecord
        {
            identity = new OdinId("seed-followee.example.com"),
            driveId = Guid.NewGuid(),
        });
    }

    private static async Task SeedFollowsMeAsync(IdentityDatabase db)
    {
        await db.FollowsMe.InsertAsync(new FollowsMeRecord
        {
            identity = "seed-follower.example.com",
            driveId = Guid.NewGuid(),
        });
    }

    private static async Task SeedInboxAsync(IdentityDatabase db)
    {
        await db.Inbox.InsertAsync(new InboxRecord
        {
            boxId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            priority = 0,
            value = null,
        });
    }

    private static async Task SeedOutboxAsync(IdentityDatabase db)
    {
        await db.Outbox.InsertAsync(new OutboxRecord
        {
            driveId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            recipient = "seed-recipient.example.com",
            priority = 0,
            dependencyFileId = null,
            value = null,
        });
    }

    private static async Task SeedKeyValueAsync(IdentityDatabase db)
    {
        await db.KeyValue.InsertAsync(new KeyValueRecord
        {
            key = FreshKey(),
            data = null,
        });
    }

    private static async Task SeedKeyTwoValueAsync(IdentityDatabase db)
    {
        await db.KeyTwoValue.InsertAsync(new KeyTwoValueRecord
        {
            key1 = FreshKey(),
            key2 = FreshKey(),
            data = null,
        });
    }

    private static async Task SeedKeyThreeValueAsync(IdentityDatabase db)
    {
        await db.KeyThreeValue.InsertAsync(new KeyThreeValueRecord
        {
            key1 = FreshKey(),
            key2 = FreshKey(),
            key3 = FreshKey(),
            data = null,
        });
    }

    private static async Task SeedKeyUniqueThreeValueAsync(IdentityDatabase db)
    {
        await db.KeyUniqueThreeValue.InsertAsync(new KeyUniqueThreeValueRecord
        {
            key1 = FreshKey(),
            key2 = FreshKey(),
            key3 = FreshKey(),
            data = null,
        });
    }

    private static async Task SeedNonceAsync(IdentityDatabase db)
    {
        // Nonce.InsertAsync rejects expired records, so seed with a far-future expiration.
        await db.Nonce.InsertAsync(new NonceRecord
        {
            id = Guid.NewGuid(),
            data = "seed",
            expiration = UnixTimeUtc.Now().AddSeconds(3600),
        });
    }

    //
    // System-database seeders
    //

    private static async Task SeedJobsAsync(SystemDatabase db)
    {
        await db.Jobs.InsertAsync(new JobsRecord
        {
            id = Guid.NewGuid(),
            name = "seed-job",
            state = (int)JobState.Scheduled,
            priority = 0,
            nextRun = UnixTimeUtc.Now(),
            lastRun = null,
            runCount = 0,
            maxAttempts = 1,
            retryDelay = 0,
            onSuccessDeleteAfter = 0,
            onFailureDeleteAfter = 0,
            correlationId = "seed-correlation",
            jobType = "seed.job.type",
            jobData = "{}",
            jobHash = null,
            lastError = null,
        });
    }

    private static async Task SeedCertificatesAsync(SystemDatabase db, string identityDomain)
    {
        // identityDomain is the same one passed to ImportIdentityAsync, so this row
        // passes the per-identity filter in ImportSystemTablesForIdentityAsync.
        await db.Certificates.InsertAsync(new CertificatesRecord
        {
            domain = new OdinId(identityDomain),
            privateKey = "seed-private-key",
            certificate = "seed-certificate",
            expiration = UnixTimeUtc.Now().AddSeconds(3600),
            lastAttempt = UnixTimeUtc.Now(),
            correlationId = "seed-correlation",
            lastError = null,
        });
    }

    private static async Task SeedLastSeenAsync(SystemDatabase db)
    {
        // TableLastSeen has no overridden InsertAsync, so the CRUD InsertAsync is reachable.
        await db.LastSeen.InsertAsync(new LastSeenRecord
        {
            subject = "seed-subject",
            timestamp = UnixTimeUtc.Now(),
        });
    }

    private static async Task SeedRegistrationsAsync(
        SystemDatabase db,
        string identityDomain,
        Guid identityId)
    {
        // identityId + identityDomain match what the test passes to ImportIdentityAsync,
        // so this row passes the per-identity filter in ImportSystemTablesForIdentityAsync.
        await db.Registrations.InsertAsync(new RegistrationsRecord
        {
            identityId = identityId,
            email = "seed@example.com",
            primaryDomainName = identityDomain,
            firstRunToken = null,
            disabled = false,
            markedForDeletionDate = null,
            planId = null,
            json = null,
        });
    }

    private static async Task SeedSettingsAsync(SystemDatabase db)
    {
        await db.Settings.InsertAsync(new SettingsRecord
        {
            key = "seed-key",
            value = "seed-value",
        });
    }
}
