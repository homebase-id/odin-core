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
/// <see cref="TableCircleMigrationV202610140701"/> adds CircleSlug NOT NULL and gives every existing circle
/// a readable slug during the copy (#1939).
/// </summary>
public class CircleSlugMigrationTests : IocTestBase
{
    private const long VersionBeforeCircleSlug = 202610130807;

    private static readonly Guid ChatCircleId = Guid.Parse("55900e0ab05347dca85c5ac2514e7fd3");
    private static readonly Guid OwnerConsoleAppId = Guid.Parse("ac126e09-54cb-4878-a690-856be692da16");

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task EveryCircleGetsAReadableSlugUniqueWithinItsApp(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, createDatabases: false);
        await using var scope = Services.BeginLifetimeScope();
        var migrator = scope.Resolve<IdentityMigrator>();
        await migrator.MigrateAsync(VersionBeforeCircleSlug);
        var connectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();

        var appId = Guid.NewGuid();
        var otherAppId = Guid.NewGuid();
        var chatAppId = Guid.NewGuid();

        // Two circles with one name in one app, as Filesync made: the older keeps the plain slug.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, first, "Filesync", appId);
        await InsertAsync(connectionFactory, IdentityId, second, "Filesync", appId);

        // The same name in another app does not collide.
        var otherApp = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, otherApp, "Filesync", otherAppId);

        // A user circle named "Chat", inserted before the built-in chat circle, still never takes "chat".
        var namedChat = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, namedChat, "Chat", chatAppId);
        await InsertAsync(connectionFactory, IdentityId, ChatCircleId, "Chat", chatAppId);

        // Long names are cut to 14 characters; names with nothing sluggable fall back to "circle".
        var longName = Guid.NewGuid();
        var emojiOnly = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, longName, "My Very Close Friends & Family", appId);
        await InsertAsync(connectionFactory, IdentityId, emojiOnly, "🎉🎉", appId);

        // NULL AppIds are deduplicated with the owner console's, which v18 -> v19 later stamps them to.
        var ownerless1 = Guid.NewGuid();
        var ownerless2 = Guid.NewGuid();
        var ownerConsole = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, ownerless1, "Family", null);
        await InsertAsync(connectionFactory, IdentityId, ownerless2, "Family", null);
        await InsertAsync(connectionFactory, IdentityId, ownerConsole, "Family", OwnerConsoleAppId);

        // A second identity in the same table: on Postgres every identity shares it and it is migrated
        // once, and slugs are unique per identity.
        var otherIdentitys = Guid.NewGuid();
        await InsertAsync(connectionFactory, Guid.NewGuid(), otherIdentitys, "Filesync", appId);

        await migrator.MigrateAsync();

        Assert.That(await migrator.GetCurrentVersionAsync(), Is.GreaterThanOrEqualTo(202610140701));

        var slugs = await ReadSlugsAsync(connectionFactory);
        Assert.That(slugs[first], Is.EqualTo("filesync"));
        Assert.That(slugs[second], Is.EqualTo("filesync-2"));
        Assert.That(slugs[otherApp], Is.EqualTo("filesync"));
        Assert.That(slugs[ChatCircleId], Is.EqualTo("chat"), "a built-in circle gets its fixed slug");
        Assert.That(slugs[namedChat], Is.EqualTo("chat-2"), "a built-in circle's slug is never derived for another circle");
        Assert.That(slugs[longName], Is.EqualTo("my-very-close"));
        Assert.That(slugs[emojiOnly], Is.EqualTo("circle"));
        Assert.That(slugs[ownerless1], Is.EqualTo("family"));
        Assert.That(slugs[ownerless2], Is.EqualTo("family-2"));
        Assert.That(slugs[ownerConsole], Is.EqualTo("family-3"));
        Assert.That(slugs[otherIdentitys], Is.EqualTo("filesync"));
    }

    private static async Task InsertAsync(ScopedIdentityConnectionFactory connectionFactory, Guid identityId,
        Guid circleId, string name, Guid? appId)
    {
        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Circle (identityId,circleId,circleName,data,AppId) " +
            "VALUES (@identityId,@circleId,@name,@data,@appId);";
        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        cmd.AddParameter("@circleId", DbType.Binary, circleId);
        cmd.AddParameter("@name", DbType.String, name);
        cmd.AddParameter("@data", DbType.Binary, new byte[] { 1, 2, 3 });
        cmd.AddParameter("@appId", DbType.Binary, appId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<Guid, string>> ReadSlugsAsync(ScopedIdentityConnectionFactory connectionFactory)
    {
        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT circleId,CircleSlug FROM Circle;";
        var result = new Dictionary<Guid, string>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            result[new Guid((byte[])rdr[0])] = (string)rdr[1];
        }

        return result;
    }
}
