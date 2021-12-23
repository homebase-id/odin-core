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
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Tests.Drive
{
    public class TestFile
    {
        public object MetadataJsonContent { get; set; }
        public string PayloadContentType { get; set; }
        public string PayloadData { get; set; }
        public Guid? CategoryId { get; set; }
        public bool ContentIsComplete { get; set; }
    }

    [TestFixture]
    public class DriveSearchTests
    {
        private ServiceTestScaffold _scaffold;
        private readonly byte[] _ekh_Iv;
        private readonly byte[] _ekh_Key;

        public DriveSearchTests()
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

            Array.Fill(_ekh_Iv, (byte)1);
            Array.Fill(_ekh_Key, (byte)1);
        }

        [TearDown]
        public void Cleanup()
        {
            //_scaffold.Cleanup();
        }

        [Test]
        public async Task CanSearchRecentFiles()
        {
            var driveService = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);
            var queryService = new DriveQueryService(driveService, null, _scaffold.LoggerFactory);

            const string driveName = "Test-Drive";
            var storageDrive = await driveService.CreateDrive(driveName);
            Assert.IsNotNull(storageDrive);

            var driveId = storageDrive.Id;
            Guid categoryId = Guid.Parse("531e832a-d3fa-4b37-b004-2e2b67a70971");

            await AddFile(driveService, driveId, new TestFile()
            {
                MetadataJsonContent = new { message = "We're going to the beach" },
                PayloadContentType = "text/plain",
                PayloadData = "this is a text payload",
                CategoryId = categoryId
            });

            object file2MetadataContent = new { message = "some data" };
            await AddFile(driveService, driveId, new TestFile()
            {
                MetadataJsonContent = file2MetadataContent,
                PayloadContentType = "application/json",
                PayloadData = JsonConvert.SerializeObject(new { data = "some data", number = 42 }),
                CategoryId = categoryId
            });
            
            await AddFile(driveService, driveId, new TestFile()
            {
                MetadataJsonContent = new { message = "more data goes here..." },
                ContentIsComplete = false,
                PayloadContentType = "application/json",
                PayloadData = JsonConvert.SerializeObject(new { data = "more data goes here and there and everywhere", number = 42 }),
                CategoryId = null
            });
            
            //test the indexing
            var itemsByCategory = await queryService.GetItemsByCategory(driveId, categoryId, true, PageOptions.All);
            Assert.That(itemsByCategory.Results.Count, Is.EqualTo(2));
            Assert.IsNotNull(itemsByCategory.Results.SingleOrDefault(item=>item.JsonContent == JsonConvert.SerializeObject(file2MetadataContent)));
            

            var recentItems = await queryService.GetRecentlyCreatedItems(driveId, true, PageOptions.All);
            
            Assert.That(recentItems.Results.Count, Is.EqualTo(3));

        }

        private async Task<DriveFileId> AddFile(DriveService driveService, Guid driveId, TestFile testFile)
        {
            var file = driveService.CreateFileId(driveId);

            var keyHeader = KeyHeader.NewRandom16();
            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, _ekh_Iv, _ekh_Key);
            await driveService.WriteKeyHeader(file, ekh, StorageDisposition.LongTerm);

            var metadata = new FileMetaData()
            {
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ContentType = testFile.PayloadContentType,
                AppData = new AppFileMetaData()
                {
                    CategoryId = testFile.CategoryId,
                    ContentIsComplete = testFile.ContentIsComplete,
                    JsonContent = JsonConvert.SerializeObject(testFile.MetadataJsonContent)
                }
            };

            await driveService.WriteMetaData(file, metadata, StorageDisposition.LongTerm);

            var payloadCipherStream = keyHeader.GetEncryptedStreamAes(testFile.PayloadData);
            await driveService.WritePayload(file, payloadCipherStream, StorageDisposition.LongTerm);

            var storedEkh = await driveService.GetKeyHeader(file, StorageDisposition.LongTerm);

            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(ekh.Data, storedEkh.Data));
            ByteArrayUtil.EquiByteArrayCompare(ekh.Iv, storedEkh.Iv);
            Assert.IsTrue(ekh.Type == storedEkh.Type);
            Assert.IsTrue(ekh.EncryptionVersion == storedEkh.EncryptionVersion);

            var storedMetadata = await driveService.GetMetadata(file, StorageDisposition.LongTerm);

            Assert.IsTrue(metadata.Created == storedMetadata.Created);
            Assert.IsTrue(metadata.ContentType == storedMetadata.ContentType);

            Assert.IsTrue(metadata.Updated < storedMetadata.Updated); //write payload updates metadata
            Assert.IsNotNull(storedMetadata.AppData);
            Assert.IsTrue(metadata.AppData.CategoryId == storedMetadata.AppData.CategoryId);
            Assert.IsTrue(metadata.AppData.ContentIsComplete == storedMetadata.AppData.ContentIsComplete);
            Assert.IsTrue(metadata.AppData.JsonContent == storedMetadata.AppData.JsonContent);

            await using var storedPayload = await driveService.GetPayloadStream(file, StorageDisposition.LongTerm);
            var storedPayloadBytes = StreamToBytes(storedPayload);
            var payloadCipherBytes = StreamToBytes(payloadCipherStream);
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadCipherBytes, storedPayloadBytes));

            return file;
        }

        private byte[] StreamToBytes(Stream stream)
        {
            MemoryStream ms = new();
            stream.Position = 0; //reset due to other readers
            stream.CopyToAsync(ms).GetAwaiter().GetResult();
            return ms.ToArray();
        }
    }
}