using System;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Services.Tests.KeyValueStorage;

public class TwoKeyValueStorageTests
{
    [Test]
    public void RequireNonEmptyContextKey()
    {
        var finalPath = "TwoKeyValueStorageTests001";
        using var db = new IdentityDatabase(Guid.NewGuid(), finalPath);
        using var myc = db.CreateDisposableConnection();
        db.CreateDatabase(false);
        Assert.Throws<OdinSystemException>(() => { new TwoKeyValueStorage(Guid.Empty); });
    }

    [Test]
    public void CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey()
    {
        var finalPath = "TwoKeyValueStorageTests002";
        using var db = new IdentityDatabase(Guid.NewGuid(), finalPath);

        db.CreateDatabase(true);

        var contextKey1 = Guid.NewGuid();
        var dataTypeKey = Guid.NewGuid().ToByteArray();
        var kvp1 = new TwoKeyValueStorage(contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        kvp1.Upsert(db, pk, dataTypeKey, expectedValue1);
        Assert.IsTrue(kvp1.Get<string>(db, pk) == expectedValue1);

        kvp1.Delete(db, pk);
        Assert.IsTrue(kvp1.Get<string>(db, pk) == null);

        var contextKey2 = Guid.NewGuid();
        var kvp2 = new TwoKeyValueStorage(contextKey2);
        const string expectedValue2 = "another value";
        kvp2.Upsert(db, pk, dataTypeKey, expectedValue2);
        Assert.IsTrue(kvp2.Get<string>(db, pk) == expectedValue2);

        kvp2.Delete(db, pk);
        Assert.IsTrue(kvp2.Get<string>(db, pk) == null);
    }
}
