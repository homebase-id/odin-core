using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableDrivesCachedTests : IocTestBase
{
    [Test]
    [TestCase(false)]
#if RUN_REDIS_TESTS
    [TestCase(true)]
#endif
    public async Task ItShouldTestCachingFromAtoZ(bool redisEnabled)
    {
        await RegisterServicesAsync(DatabaseType.Sqlite, redisEnabled: redisEnabled);
        await using var scope = Services.BeginLifetimeScope();
        var tableDrivesCached = scope.Resolve<TableDrivesCached>();

        // NOTE: all this key stuff is just to get the DB validation to pass. Don't use this as an example to do crypto.
        var mk = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
        var secret = new SensitiveByteArray(mk.GetKey());
        var key = new SymmetricKeyEncryptedAes(secret);
        var driveKey = new SymmetricKeyEncryptedAes(key);
        var storageKey = driveKey.DecryptKeyClone(mk);
        var (encryptedIdIv, encryptedIdValue) = AesCbc.Encrypt(Guid.NewGuid().ToByteArray(), storageKey);

        var item1 = new DrivesRecord
        {
            DriveId = Guid.NewGuid(),
            StorageKeyCheckValue = Guid.NewGuid(),
            DriveType = Guid.NewGuid(),
            DriveName = "My Drive 1",
            MasterKeyEncryptedStorageKeyJson = OdinSystemSerializer.Serialize(driveKey),
            EncryptedIdIv64 = encryptedIdIv.ToBase64(),
            EncryptedIdValue64 = encryptedIdValue.ToBase64(),
            detailsJson = OdinSystemSerializer.Serialize("whatever"),
        };

        {
            var record = await tableDrivesCached.GetAsync(item1.DriveId, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Null);
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(0));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(1));
        }

        if (redisEnabled) WipeL1();

        {
            var record = await tableDrivesCached.GetAsync(item1.DriveId, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Null);
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(1));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(1));
        }

        if (redisEnabled) WipeL1();

        {
            var records = await tableDrivesCached.GetDrivesByTypeAsync(item1.DriveType, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(1));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(2));
        }

        if (redisEnabled) WipeL1();

        {
            var records = await tableDrivesCached.GetDrivesByTypeAsync(item1.DriveType, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(2));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(2));
        }

        if (redisEnabled) WipeL1();

        {
            var record = await tableDrivesCached.GetByTargetDriveAsync(item1.DriveId, item1.DriveType, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Null);
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(2));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(3));
        }

        if (redisEnabled) WipeL1();

        {
            var record = await tableDrivesCached.GetByTargetDriveAsync(item1.DriveId, item1.DriveType, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Null);
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(3));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(3));
        }

        if (redisEnabled) WipeL1();

        {
            var (records, _, _) = await tableDrivesCached.GetList(100, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(3));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(4));
        }

        if (redisEnabled) WipeL1();

        {
            var (records, _, _) = await tableDrivesCached.GetList(100, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(4));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(4));
        }

        if (redisEnabled) WipeL1();

        {
            var count = await tableDrivesCached.GetCountAsync(TimeSpan.FromMilliseconds(2000));
            Assert.That(count, Is.EqualTo(0));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(4));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(5));
        }

        if (redisEnabled) WipeL1();

        {
            var count = await tableDrivesCached.GetCountAsync(TimeSpan.FromMilliseconds(2000));
            Assert.That(count, Is.EqualTo(0));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(5));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(5));
        }

        await tableDrivesCached.InsertAsync(item1);

        if (redisEnabled) WipeL1();

        {
            var record = await tableDrivesCached.GetAsync(item1.DriveId, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(5));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(6));
        }

        if (redisEnabled) WipeL1();

        {
            var record = await tableDrivesCached.GetAsync(item1.DriveId, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(6));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(6));
        }

        if (redisEnabled) WipeL1();

        {
            var records = await tableDrivesCached.GetDrivesByTypeAsync(item1.DriveType, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(6));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(7));
        }

        if (redisEnabled) WipeL1();

        {
            var records = await tableDrivesCached.GetDrivesByTypeAsync(item1.DriveType, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(7));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(7));
        }

        if (redisEnabled) WipeL1();

        {
            var record = await tableDrivesCached.GetByTargetDriveAsync(item1.DriveId, item1.DriveType, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(7));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(8));
        }

        if (redisEnabled) WipeL1();

        {
            var record = await tableDrivesCached.GetByTargetDriveAsync(item1.DriveId, item1.DriveType, TimeSpan.FromMilliseconds(2000));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(8));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(8));
        }

        if (redisEnabled) WipeL1();

        {
            var (records, _, _) = await tableDrivesCached.GetList(100, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(8));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(9));
        }

        if (redisEnabled) WipeL1();

        {
            var (records, _, _) = await tableDrivesCached.GetList(100, null, TimeSpan.FromMilliseconds(2000));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(9));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(9));
        }

        if (redisEnabled) WipeL1();

        {
            var count = await tableDrivesCached.GetCountAsync(TimeSpan.FromMilliseconds(2000));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(9));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(10));
        }

        if (redisEnabled) WipeL1();

        {
            var count = await tableDrivesCached.GetCountAsync(TimeSpan.FromMilliseconds(2000));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(tableDrivesCached.Hits, Is.EqualTo(10));
            Assert.That(tableDrivesCached.Misses, Is.EqualTo(10));
        }
    }

}


