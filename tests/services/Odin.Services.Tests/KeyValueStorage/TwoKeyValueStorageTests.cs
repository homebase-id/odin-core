using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Services.Tests.KeyValueStorage;

public class TwoKeyValueStorageTests
{
    [Test]
    public async Task RequireNonEmptyContextKey()
    {
        var finalPath = "TwoKeyValueStorageTests001";
        using var db = new IdentityDatabase(Guid.NewGuid(), finalPath);
        using var myc = db.CreateDisposableConnection();
        await db.CreateDatabaseAsync(false);
        Assert.Throws<OdinSystemException>(() => { new TwoKeyValueStorage(Guid.Empty); });
    }

    [Test]
    public async Task CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey()
    {
        var finalPath = "TwoKeyValueStorageTests002";
        using var db = new IdentityDatabase(Guid.NewGuid(), finalPath);

        await db.CreateDatabaseAsync(true);

        var contextKey1 = Guid.NewGuid();
        var dataTypeKey = Guid.NewGuid().ToByteArray();
        var kvp1 = new TwoKeyValueStorage(contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        await kvp1.UpsertAsync(db, pk, dataTypeKey, expectedValue1);
        Assert.IsTrue(await kvp1.GetAsync<string>(db, pk) == expectedValue1);

        await kvp1.DeleteAsync(db, pk);
        Assert.IsTrue(await kvp1.GetAsync<string>(db, pk) == null);

        var contextKey2 = Guid.NewGuid();
        var kvp2 = new TwoKeyValueStorage(contextKey2);
        const string expectedValue2 = "another value";
        await kvp2.UpsertAsync(db, pk, dataTypeKey, expectedValue2);
        Assert.IsTrue(await kvp2.GetAsync<string>(db, pk) == expectedValue2);

        await kvp2.DeleteAsync(db, pk);
        Assert.IsTrue(await kvp2.GetAsync<string>(db, pk) == null);
    }
}
