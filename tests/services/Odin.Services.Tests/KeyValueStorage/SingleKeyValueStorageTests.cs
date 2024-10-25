using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Services.Tests.KeyValueStorage;

public class SingleKeyValueStorageTests
{
    [Test]
    public async Task RequireNonEmptyContextKey()
    {
        var finalPath = "SingleKeyValueStorageTests001";
        using var db = new IdentityDatabase(Guid.NewGuid(), finalPath);
        await db.CreateDatabaseAsync(true);
        Assert.Throws<OdinSystemException>(() => { new SingleKeyValueStorage(Guid.Empty); });
    }

    [Test]
    public async Task CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey()
    {
        using var db = new IdentityDatabase(Guid.NewGuid(), "SingleKeyValueStorageTests002");
        await db.CreateDatabaseAsync(true);

        var contextKey1 = Guid.NewGuid();
        var singleKvp1 = new SingleKeyValueStorage(contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        await singleKvp1.UpsertAsync(db, pk, expectedValue1);
        Assert.IsTrue(await singleKvp1.GetAsync<string>(db, pk) == expectedValue1);
        await singleKvp1.DeleteAsync(db, pk);
        Assert.IsTrue(await singleKvp1.GetAsync<string>(db, pk) == null);

        var contextKey2 = Guid.NewGuid();
        var singleKvp2 = new SingleKeyValueStorage(contextKey2);
        const string expectedValue2 = "another value";
        await singleKvp2.UpsertAsync(db, pk, expectedValue2);
        Assert.IsTrue(await singleKvp2.GetAsync<string>(db, pk) == expectedValue2);

        await singleKvp2.DeleteAsync(db, pk);
        Assert.IsTrue(await singleKvp2.GetAsync<string>(db, pk) == null);
    }
}