using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NuGet.Frameworks;
using NUnit.Framework;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Tests.Drive
{
    [TestFixture]
    public class DriveStorageTests
    {
        private ServiceTestScaffold _scaffold;
        private readonly byte[] _ekh_Iv;
        private readonly byte[] _ekh_Key;

        public DriveStorageTests()
        {
            _ekh_Key = new byte[16];
            _ekh_Iv = new byte[16];
        }

        [SetUp]
        public void Setup()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new ServiceTestScaffold(folder);
            _scaffold.CreateContext();
            _scaffold.CreateSystemStorage();
            _scaffold.CreateLoggerFactory();
            _scaffold.CreateMediator();

            Array.Fill(_ekh_Iv, (byte)1);
            Array.Fill(_ekh_Key, (byte)1);
        }

        [TearDown]
        public void Cleanup()
        {
            //_scaffold.Cleanup();
        }

        [Test]
        public async Task CanStoreLongTermFile()
        {
            var driveService = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory,_scaffold.Mediator);

            const string driveName = "Test-Drive";
            var storageDrive = await driveService.CreateDrive(driveName);
            Assert.IsNotNull(storageDrive);
            Assert.IsTrue(Directory.Exists(storageDrive.GetStoragePath(StorageDisposition.Temporary)));
            Assert.IsTrue(Directory.Exists(storageDrive.GetStoragePath(StorageDisposition.LongTerm)));
            Assert.IsTrue(Directory.Exists(storageDrive.GetIndexPath()));

            Assert.That(storageDrive.EncryptedIdValue, Is.Not.Null);
            Assert.That(storageDrive.EncryptedIdValue, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

            Assert.That(storageDrive.EncryptedIdIv, Is.Not.Null);
            Assert.That(storageDrive.EncryptedIdIv, Is.Not.EqualTo(Guid.Empty.ToByteArray()));

            var mk = _scaffold!.Context!.Caller.GetMasterKey();
            var storageKey = storageDrive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref mk);

            var decryptedDriveId = AesCbc.Decrypt(storageDrive.EncryptedIdValue, ref storageKey, storageDrive.EncryptedIdIv);
            Assert.That(decryptedDriveId, Is.EqualTo(storageDrive.Id.ToByteArray()));

            var file = driveService.CreateFileId(storageDrive.Id);

            var keyHeader = KeyHeader.NewRandom16();
            var sba = _ekh_Key.ToSensitiveByteArray();
            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, _ekh_Iv, ref sba);
            await driveService.WriteEncryptedKeyHeader(file, ekh);

            var metadata = new FileMetadata(file)
            {
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ContentType = "application/json",
                AppData = new AppFileMetaData()
                {
                    PrimaryCategoryId = Guid.Empty,
                    ContentIsComplete = true,
                    JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach" })
                }
            };

            await driveService.WriteMetaData(file, metadata);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipherStream = keyHeader.GetEncryptedStreamAes(payloadData);
            await driveService.WritePayload(file, payloadCipherStream);

            var storedEkh = await driveService.GetEncryptedKeyHeader(file);

            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(ekh.EncryptedAesKey, storedEkh.EncryptedAesKey));
            ByteArrayUtil.EquiByteArrayCompare(ekh.Iv, storedEkh.Iv);
            Assert.IsTrue(ekh.Type == storedEkh.Type);
            Assert.IsTrue(ekh.EncryptionVersion == storedEkh.EncryptionVersion);

            var storedMetadata = await driveService.GetMetadata(file);

            Assert.IsTrue(metadata.Created == storedMetadata.Created);
            Assert.IsTrue(metadata.ContentType == storedMetadata.ContentType);
            
            Assert.IsTrue(metadata.Updated < storedMetadata.Updated); //write payload updates metadata
            Assert.IsNotNull(storedMetadata.AppData);
            Assert.IsTrue(metadata.AppData.PrimaryCategoryId == storedMetadata.AppData.PrimaryCategoryId);
            Assert.IsTrue(metadata.AppData.ContentIsComplete == storedMetadata.AppData.ContentIsComplete);
            Assert.IsTrue(metadata.AppData.JsonContent == storedMetadata.AppData.JsonContent);

            await using var storedPayload = await driveService.GetPayloadStream(file);
            var storedPayloadBytes = StreamToBytes(storedPayload);
            
            var payloadCipherBytes = StreamToBytes(payloadCipherStream);
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadCipherBytes, storedPayloadBytes));
        }

        private byte[] StreamToBytes(Stream stream)
        {
            MemoryStream ms = new ();
            stream.Position = 0; //reset due to other readers
            stream.CopyToAsync(ms).GetAwaiter().GetResult();
            return ms.ToArray();
        }
    }
}