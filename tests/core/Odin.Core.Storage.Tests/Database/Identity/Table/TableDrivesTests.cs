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
    }
}
