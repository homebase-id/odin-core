using System;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Services.Tests.KeyValueStorage;

public class ThreeKeyValueStorageTests
{
    [Test]
    public void RequireNonEmptyContextKey()
    {
        var finalPath = "testdb1.db";
        var db = new IdentityDatabase(finalPath);
        db.CreateDatabase(false);

        Assert.Throws<OdinSystemException>(() => { new ThreeKeyValueStorage(db.TblKeyThreeValue, Guid.Empty); });
        db.Dispose();
    }

    [Test]
    public void CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey()
    {
        var finalPath = "testdb2.db";
        var db = new IdentityDatabase(finalPath);
        db.CreateDatabase(false);

        var contextKey1 = Guid.NewGuid();
        var dataTypeKey = Guid.NewGuid().ToByteArray();
        var dataCategoryKey = Guid.NewGuid().ToByteArray();
        var kvp1 = new ThreeKeyValueStorage(db.TblKeyThreeValue, contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        kvp1.Upsert(pk, dataTypeKey, dataCategoryKey, expectedValue1);
        Assert.IsTrue(kvp1.Get<string>(pk) == expectedValue1);

        kvp1.Delete(pk);
        Assert.IsTrue(kvp1.Get<string>(pk) == null);

        var contextKey2 = Guid.NewGuid();
        var kvp2 = new ThreeKeyValueStorage(db.TblKeyThreeValue, contextKey2);
        const string expectedValue2 = "another value";
        kvp2.Upsert(pk, dataTypeKey, dataCategoryKey, expectedValue2);
        Assert.IsTrue(kvp2.Get<string>(pk) == expectedValue2);
        
        kvp2.Delete(pk);
        Assert.IsTrue(kvp2.Get<string>(pk) == null);

        
        db.Dispose();

    }
}