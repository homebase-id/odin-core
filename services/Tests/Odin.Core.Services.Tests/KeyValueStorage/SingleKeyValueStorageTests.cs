using System;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Services.Tests.KeyValueStorage;

public class SingleKeyValueStorageTests
{
    [Test]
    public void RequireNonEmptyContextKey()
    {
        var finalPath = "testdb1.db";
        var db = new IdentityDatabase($"Data Source={finalPath}");
        db.CreateDatabase(false);

        Assert.Throws<ArgumentException>(() => { new SingleKeyValueStorage(db.tblKeyValue, Guid.Empty); });
        db.Dispose();
    }

    [Test]
    public void CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey()
    {
        var finalPath = "testdb2.db";
        var db = new IdentityDatabase($"Data Source={finalPath}");
        db.CreateDatabase(false);

        var contextKey1 = Guid.NewGuid();
        var singleKvp1 = new SingleKeyValueStorage(db.tblKeyValue, contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        singleKvp1.Upsert(pk, expectedValue1);
        Assert.IsTrue(singleKvp1.Get<string>(pk) == expectedValue1);
        singleKvp1.Delete(pk);
        Assert.IsTrue(singleKvp1.Get<string>(pk) == null);

        var contextKey2 = Guid.NewGuid();
        var singleKvp2 = new SingleKeyValueStorage(db.tblKeyValue, contextKey2);
        const string expectedValue2 = "another value";
        singleKvp2.Upsert(pk, expectedValue2);
        Assert.IsTrue(singleKvp2.Get<string>(pk) == expectedValue2);
        
        singleKvp2.Delete(pk);
        Assert.IsTrue(singleKvp2.Get<string>(pk) == null);

        
        db.Dispose();

    }
}