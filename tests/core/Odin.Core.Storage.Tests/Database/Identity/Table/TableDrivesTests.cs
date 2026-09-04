using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableDrivesTests : IocTestBase
    {
        private DrivesRecord CreateDrivesRecord()
        {
            var mk = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var secret = new SensitiveByteArray(mk.GetKey());
            var key = new SymmetricKeyEncryptedAes(secret);
            var driveKey = new SymmetricKeyEncryptedAes(key);
            var storageKey = driveKey.DecryptKeyClone(mk);
            var (encryptedIdIv, encryptedIdValue) = AesCbc.Encrypt(Guid.NewGuid().ToByteArray(), storageKey);

            return new DrivesRecord
            {
                DriveId = Guid.NewGuid(),
                StorageKeyCheckValue = Guid.NewGuid(),
                DriveType = Guid.NewGuid(),
                DriveName = "Drive " + Guid.NewGuid(),
                MasterKeyEncryptedStorageKeyJson = OdinSystemSerializer.Serialize(driveKey),
                EncryptedIdIv64 = encryptedIdIv.ToBase64(),
                EncryptedIdValue64 = encryptedIdValue.ToBase64(),
                detailsJson = OdinSystemSerializer.Serialize("details"),
            };
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task PagingByRowIdTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableDrives>();

            await tbl.InsertAsync(CreateDrivesRecord());
            await tbl.InsertAsync(CreateDrivesRecord());
            await tbl.InsertAsync(CreateDrivesRecord());

            var (page1, cursor1) = await tbl.PagingByRowIdAsync(2, null);
            Assert.That(page1.Count, Is.EqualTo(2));
            Assert.That(cursor1, Is.Not.Null);

            var (page2, cursor2) = await tbl.PagingByRowIdAsync(2, cursor1);
            Assert.That(page2.Count, Is.EqualTo(1));
            Assert.That(cursor2, Is.Null);

            var (all, allCursor) = await tbl.PagingByRowIdAsync(100, null);
            Assert.That(all.Count, Is.EqualTo(3));
            Assert.That(allCursor, Is.Null);
        }
        /// <summary>
        /// The three addressing columns are dormant -- nothing derives them yet -- but they are columns
        /// rather than detailsJson fields, so what goes in must come back out without a round trip
        /// through the blob.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task AddressingColumnsRoundTrip(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableDrives>();

            var appId = Guid.NewGuid();

            var record = CreateDrivesRecord();
            record.AppId = appId;
            record.DriveSlug = "messages";
            record.DriveTypeSlug = "channel";
            await tbl.InsertAsync(record);

            var loaded = await tbl.GetAsync(record.DriveId);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.AppId, Is.EqualTo(appId));
            Assert.That(loaded.DriveSlug, Is.EqualTo("messages"));
            Assert.That(loaded.DriveTypeSlug, Is.EqualTo("channel"));

            // Unset is the state every drive is in today, and null must survive as null rather than
            // arriving as an empty string.
            var unslugged = CreateDrivesRecord();
            await tbl.InsertAsync(unslugged);

            var loadedUnslugged = await tbl.GetAsync(unslugged.DriveId);

            Assert.That(loadedUnslugged.AppId, Is.Null);
            Assert.That(loadedUnslugged.DriveSlug, Is.Null);
            Assert.That(loadedUnslugged.DriveTypeSlug, Is.Null);
        }
    }
}