using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Migrations;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Migrations;

/// <summary>
/// <see cref="TableDrivesMigrationV202610130807"/> fills every NULL drive address before it makes the
/// columns NOT NULL (#1901), so an identity whose owner never signed in to run v18 -&gt; v19 is not stuck.
/// </summary>
public class DrivesNotNullMigrationTests : IocTestBase
{
    private const long VersionBeforeNotNull = 202609241200;

    private static readonly Guid OwnerConsoleAppId = Guid.Parse("ac126e09-54cb-4878-a690-856be692da16");
    private static readonly Guid ChatDriveAlias = Guid.Parse("9ff813aff2d61e2f9b9db189e72d1a11");
    private static readonly Guid FeedAppId = Guid.Parse("5f887d80-0132-4294-ba40-bda79155551d");
    private static readonly Guid ChannelDriveType = Guid.Parse("8f448716-e34c-edf9-0141-45e043ca6612");

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task NullAddressesAreFilledThenTheColumnsBecomeNotNull(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, createDatabases: false);
        await using var scope = Services.BeginLifetimeScope();
        var migrator = scope.Resolve<IdentityMigrator>();
        await migrator.MigrateAsync(VersionBeforeNotNull);
        var connectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();

        var ownerless = Guid.NewGuid();
        var appOwned = Guid.NewGuid();
        var collides = Guid.NewGuid();
        var appId = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, ownerless, "Field Notes", null, null, null);
        await InsertAsync(connectionFactory, IdentityId, appOwned, "App Notes", appId, "app-notes", null);
        await InsertAsync(connectionFactory, IdentityId, ChatDriveAlias, "Chat Drive", null, null, null);

        // Already holds "notes" for the owner console; the ownerless drive carrying "notes" must move.
        await InsertAsync(connectionFactory, IdentityId, Guid.NewGuid(), "Notes", OwnerConsoleAppId, "notes", "drive");
        await InsertAsync(connectionFactory, IdentityId, collides, "Notes", null, "notes", null);

        // User channel drives go to the feed app, as v13 -> v14 gave them.  One named "Feed" must not
        // take "feed": v13 -> v14 later moves the built-in feed drive into the feed app under that slug.
        var channel = Guid.NewGuid();
        var channelNamedFeed = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, channel, "Travel Blog", null, null, null, ChannelDriveType);
        await InsertAsync(connectionFactory, IdentityId, channelNamedFeed, "Feed", null, null, null, ChannelDriveType);

        // A second identity in the same table: on Postgres every identity shares it and it is migrated
        // once, so the fill has to cover them all.
        var otherIdentitysDrive = Guid.NewGuid();
        await InsertAsync(connectionFactory, Guid.NewGuid(), otherIdentitysDrive, "Field Notes", null, null, null);

        await migrator.MigrateAsync();

        Assert.That(await migrator.GetCurrentVersionAsync(), Is.GreaterThanOrEqualTo(202610130807));

        var rows = await ReadAsync(connectionFactory);
        Assert.That(rows[ownerless], Is.EqualTo((OwnerConsoleAppId, "field-notes", "drive")));
        Assert.That(rows[appOwned], Is.EqualTo((appId, "app-notes", "drive")),
            "an owner and slug already there are kept; only the missing type slug is filled");
        Assert.That(rows[ChatDriveAlias], Is.EqualTo((OwnerConsoleAppId, "chat", "chat")),
            "a built-in drive gets its fixed address");
        Assert.That(rows[collides].DriveSlug, Is.EqualTo("notes-2"));
        Assert.That(rows[channel], Is.EqualTo((FeedAppId, "travel-blog", "channel")));
        Assert.That(rows[channelNamedFeed], Is.EqualTo((FeedAppId, "feed-2", "channel")),
            "a built-in drive's slug is never derived for another drive");
        Assert.That(rows[otherIdentitysDrive], Is.EqualTo((OwnerConsoleAppId, "field-notes", "drive")));
    }

    private static async Task InsertAsync(ScopedIdentityConnectionFactory connectionFactory, Guid identityId,
        Guid driveId, string name, Guid? appId, string driveSlug, string driveTypeSlug, Guid? driveType = null)
    {
        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Drives (identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName," +
            "MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,AppId,DriveSlug," +
            "DriveTypeSlug,created,modified) VALUES (@identityId,@driveId,@check,@driveType,@name,'{}','iv','id','{}'," +
            "@appId,@driveSlug,@driveTypeSlug,1,1);";
        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        cmd.AddParameter("@driveId", DbType.Binary, driveId);
        cmd.AddParameter("@check", DbType.Binary, Guid.NewGuid());
        cmd.AddParameter("@driveType", DbType.Binary, driveType ?? Guid.NewGuid());
        cmd.AddParameter("@name", DbType.String, name);
        cmd.AddParameter("@appId", DbType.Binary, appId);
        cmd.AddParameter("@driveSlug", DbType.String, driveSlug);
        cmd.AddParameter("@driveTypeSlug", DbType.String, driveTypeSlug);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<Guid, (Guid AppId, string DriveSlug, string DriveTypeSlug)>> ReadAsync(
        ScopedIdentityConnectionFactory connectionFactory)
    {
        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT DriveId,AppId,DriveSlug,DriveTypeSlug FROM Drives;";
        var result = new Dictionary<Guid, (Guid, string, string)>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            result[new Guid((byte[])rdr[0])] = (new Guid((byte[])rdr[1]), (string)rdr[2], (string)rdr[3]);
        }

        return result;
    }
}
