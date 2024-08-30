using System;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Services.Tests.KeyValueStorage;

public class SingleKeyValueStorageTests
{
    [Test]
    public void RequireNonEmptyContextKey()
    {
        var finalPath = "SingleKeyValueStorageTests001";
        using var db = new IdentityDatabase(Guid.NewGuid(), finalPath);
        using var myc = db.CreateDisposableConnection();
        db.CreateDatabase(true);
        Assert.Throws<OdinSystemException>(() => { new SingleKeyValueStorage(Guid.Empty); });
    }

    [Test]
    public void CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey()
    {
        using var db = new IdentityDatabase(Guid.NewGuid(), "SingleKeyValueStorageTests002");
        using var myc = db.CreateDisposableConnection();
        db.CreateDatabase(true);

        var contextKey1 = Guid.NewGuid();
        var singleKvp1 = new SingleKeyValueStorage(contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        singleKvp1.Upsert(myc, pk, expectedValue1);
        Assert.IsTrue(singleKvp1.Get<string>(myc, pk) == expectedValue1);
        singleKvp1.Delete(myc, pk);
        Assert.IsTrue(singleKvp1.Get<string>(myc, pk) == null);

        var contextKey2 = Guid.NewGuid();
        var singleKvp2 = new SingleKeyValueStorage(contextKey2);
        const string expectedValue2 = "another value";
        singleKvp2.Upsert(myc, pk, expectedValue2);
        Assert.IsTrue(singleKvp2.Get<string>(myc, pk) == expectedValue2);

        singleKvp2.Delete(myc, pk);
        Assert.IsTrue(singleKvp2.Get<string>(myc, pk) == null);
    }
}