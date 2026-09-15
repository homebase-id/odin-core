using System;
using System.Data;
using Odin.Core.Storage.Database.Identity.Connection;
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
        internal DrivesRecord CreateDrivesRecord()
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

        /// <summary>
        /// The count must be scoped to this identity. The generated CRUD's GetCountAsync is a bare
        /// "SELECT COUNT(*) FROM Drives" with no identityId filter, which is accidentally right on
        /// SQLite (one database file per tenant) and returns the whole fleet's drives on Postgres,
        /// where every tenant shares one database.
        /// </summary>
        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task GetCountAsyncIsScopedToTheIdentity(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tbl = scope.Resolve<TableDrives>();

            await tbl.InsertAsync(CreateDrivesRecord());
            await tbl.InsertAsync(CreateDrivesRecord());

            // A drive belonging to a different identity. Inserted with raw SQL because the table
            // wrapper always stamps the ambient identity onto the record.
            await InsertForeignIdentityDriveAsync(scope, Guid.NewGuid());

            Assert.That(await tbl.GetCountAsync(), Is.EqualTo(2));
        }

        private static async Task InsertForeignIdentityDriveAsync(ILifetimeScope scope, Guid foreignIdentityId)
        {
            var record = new TableDrivesTests().CreateDrivesRecord();
            var factory = scope.Resolve<ScopedIdentityConnectionFactory>();
            await using var cn = await factory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();

            cmd.CommandText =
                """
                INSERT INTO drives (identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName,
                                    MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,
                                    detailsJson,created,modified)
                VALUES (@identityId,@DriveId,@StorageKeyCheckValue,@DriveType,@DriveName,
                        @MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,
                        @detailsJson,0,0);
                """;

            cmd.AddParameter("@identityId", DbType.Binary, foreignIdentityId);
            cmd.AddParameter("@DriveId", DbType.Binary, record.DriveId);
            cmd.AddParameter("@StorageKeyCheckValue", DbType.Binary, record.StorageKeyCheckValue);
            cmd.AddParameter("@DriveType", DbType.Binary, record.DriveType);
            cmd.AddParameter("@DriveName", DbType.String, record.DriveName);
            cmd.AddParameter("@MasterKeyEncryptedStorageKeyJson", DbType.String, record.MasterKeyEncryptedStorageKeyJson);
            cmd.AddParameter("@EncryptedIdIv64", DbType.String, record.EncryptedIdIv64);
            cmd.AddParameter("@EncryptedIdValue64", DbType.String, record.EncryptedIdValue64);
            cmd.AddParameter("@detailsJson", DbType.String, record.detailsJson);

            await cmd.ExecuteNonQueryAsync();
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