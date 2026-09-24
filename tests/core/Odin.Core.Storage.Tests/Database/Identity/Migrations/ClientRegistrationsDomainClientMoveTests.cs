using System;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Migrations;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Migrations;

/// <summary>
/// The data move in <see cref="TableClientRegistrationsMigrationV202609241200"/>: YouAuth domain
/// clients leave the key-value store for the client-registrations table, each sized by its domain's
/// consent, so nobody signed into a third-party site is signed out by the switch.
/// </summary>
/// <remarks>
/// The database is stood at the version before the move, seeded with rows in the exact shape the
/// service wrote there (a key of id-plus-context, the client's JSON as the value), and then migrated
/// to the latest version, the way a server start does it.
/// </remarks>
public class ClientRegistrationsDomainClientMoveTests : IocTestBase
{
    private const long VersionBeforeTheMove = 202510201056;

    private static readonly byte[] ClientContextKey = Guid.Parse("8994c20a-179c-469c-a3b9-c4d6a8d2eb3c").ToByteArray();
    private static readonly byte[] ClientDataType = Guid.Parse("cd16bc37-3e1f-410b-be03-7bec83dd6c33").ToByteArray();
    private static readonly byte[] DomainContextKey = Guid.Parse("e11ff091-0edf-4532-8b0f-b9d9ebe0880f").ToByteArray();

    /// <summary>
    /// The migration's own frozen copy of the sliding lifetime; the test pins the value, as it pins
    /// the GUIDs below, so a change in the service does not silently change what the move does.
    /// </summary>
    private static readonly TimeSpan SixMonths = TimeSpan.FromDays(180);
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(30);

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task DomainClientsAreCarriedAcrossSizedByTheirConsent(DatabaseType databaseType)
    {
        await using var scope = await StandBeforeTheMoveAsync(databaseType);
        var migrator = scope.Resolve<IdentityMigrator>();

        var keyValues = scope.Resolve<TableKeyThreeValue>();
        var before = UnixTimeUtc.Now();

        // Four domains, one per consent case, a client under each.
        var noDate = await SeedAsync(keyValues, "never.example.org", """{"consentRequirementType":"never","expiration":0}""");
        var askEveryTime = await SeedAsync(keyValues, "always.example.org", """{"consentRequirementType":"always","expiration":0}""");
        var untilDate = before.AddDays(14);
        var dated = await SeedAsync(keyValues, "dated.example.org", $$"""{"consentRequirementType":"expiring","expiration":{{untilDate.milliseconds}}}""");
        var lapsed = await SeedAsync(keyValues, "lapsed.example.org", $$"""{"consentRequirementType":"expiring","expiration":{{before.AddDays(-1).milliseconds}}}""");

        await migrator.MigrateAsync();

        var registrations = scope.Resolve<TableClientRegistrations>();

        var neverRow = await registrations.GetAsync(noDate);
        Assert.That(neverRow, Is.Not.Null, "a client under a Never consent is carried across");
        Assert.That(neverRow.catType, Is.EqualTo(408));
        Assert.That(neverRow.issuedToId, Is.EqualTo("never.example.org"));
        AssertCloseTo(neverRow.expiresAt, before.AddMilliseconds((long)SixMonths.TotalMilliseconds), "no date from the owner means six months");
        Assert.That(SlidingExpirationOf(neverRow), Is.True, "and use restarts them");

        var alwaysRow = await registrations.GetAsync(askEveryTime);
        Assert.That(alwaysRow, Is.Not.Null, "Always is about consent, not the token; it is carried like Never");
        AssertCloseTo(alwaysRow.expiresAt, before.AddMilliseconds((long)SixMonths.TotalMilliseconds), "no date from the owner means six months");
        Assert.That(SlidingExpirationOf(alwaysRow), Is.True);

        var datedRow = await registrations.GetAsync(dated);
        Assert.That(datedRow, Is.Not.Null, "a client under an Expiring consent is carried across");
        Assert.That(datedRow.expiresAt.milliseconds, Is.EqualTo(untilDate.milliseconds), "with the owner's date, exactly");
        Assert.That(SlidingExpirationOf(datedRow), Is.False, "and nothing moves it");

        Assert.That(await registrations.GetAsync(lapsed), Is.Null,
            "a client whose consent date has passed is expired, not carried");

        Assert.That(await keyValues.GetByKeyThreeAsync(ClientDataType), Has.Count.EqualTo(4),
            "the source rows are left where they were, in case the move has to be undone");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ExistingRegistrationsSurviveTheMoveNextToTheNewcomers(DatabaseType databaseType)
    {
        // The copy carries rows with their rowIds; the moved clients are appended after. On Postgres the
        // shadow table's sequence starts over, so without a resync the first newcomer would collide with
        // rowId 1. This test is the one that would catch that.
        await using var scope = await StandBeforeTheMoveAsync(databaseType);
        var migrator = scope.Resolve<IdentityMigrator>();

        var registrations = scope.Resolve<TableClientRegistrations>();
        var existing = Guid.NewGuid();
        await registrations.UpsertAsync(new ClientRegistrationsRecord
        {
            catId = existing,
            issuedToId = "frodo.example.org",
            ttl = 3600,
            expiresAt = UnixTimeUtc.Now().AddHours(1),
            categoryId = Guid.NewGuid(),
            catType = 200,
            value = "{}"
        });

        var keyValues = scope.Resolve<TableKeyThreeValue>();
        var moved = await SeedAsync(keyValues, "never.example.org", """{"consentRequirementType":"never","expiration":0}""");

        await migrator.MigrateAsync();

        Assert.That(await registrations.GetAsync(existing), Is.Not.Null, "the row that was already there");
        Assert.That(await registrations.GetAsync(moved), Is.Not.Null, "and the one that moved in");
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A fresh database migrated to the version before the move, in a scope the test owns.
    /// </summary>
    private async Task<ILifetimeScope> StandBeforeTheMoveAsync(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, createDatabases: false);
        var scope = Services.BeginLifetimeScope();
        await scope.Resolve<IdentityMigrator>().MigrateAsync(VersionBeforeTheMove);
        return scope;
    }

    /// <summary>
    /// A domain registration and one client under it, in the key-value shape the service wrote before
    /// the move. Returns the client's token id.
    /// </summary>
    private static async Task<Guid> SeedAsync(TableKeyThreeValue keyValues, string domain, string consentJson)
    {
        var domainKey = ByteArrayUtil.ReduceSHA256Hash(domain.ToLower()).ToByteArray();

        var registrationJson = $$"""{"domain":"{{domain}}","name":"{{domain}}","isRevoked":false,"consentRequirements":{{consentJson}}}""";
        await keyValues.InsertAsync(new KeyThreeValueRecord
        {
            key1 = ByteArrayUtil.Combine(domainKey, DomainContextKey),
            key2 = Guid.Empty.ToByteArray(),
            key3 = Guid.Parse("0c2c70c2-86e9-4214-818d-8b57c8d59762").ToByteArray(),
            data = Encoding.UTF8.GetBytes(registrationJson)
        });

        var tokenId = Guid.NewGuid();
        var clientJson = $$"""{"domain":"{{domain}}","accessRegistration":{"id":"{{tokenId:N}}","isRevoked":false},"friendlyName":"a browser","type":408,"timeToLiveSeconds":5184000}""";
        await keyValues.InsertAsync(new KeyThreeValueRecord
        {
            key1 = ByteArrayUtil.Combine(tokenId.ToByteArray(), ClientContextKey),
            key2 = domainKey,
            key3 = ClientDataType,
            data = Encoding.UTF8.GetBytes(clientJson)
        });

        return tokenId;
    }

    private static bool SlidingExpirationOf(ClientRegistrationsRecord row)
    {
        var value = JsonNode.Parse(row.value)!.AsObject();
        Assert.That(value["slidingExpiration"], Is.Not.Null, "the moved JSON must say whether use restarts the lifetime");
        return value["slidingExpiration"]!.GetValue<bool>();
    }

    private static void AssertCloseTo(UnixTimeUtc actual, UnixTimeUtc expected, string because)
    {
        Assert.That(actual.milliseconds, Is.EqualTo(expected.milliseconds).Within((long)Slack.TotalMilliseconds), because);
    }
}
