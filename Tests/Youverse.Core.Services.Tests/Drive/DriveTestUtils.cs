using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Tests.Drive
{
    public static class DriveTestUtils
    {
        private static readonly byte[] InitializationVector = new byte[16];
        private static readonly byte[] EncryptionKey = new byte[16];

        static DriveTestUtils()
        {
            Array.Fill(InitializationVector, (byte) 1);
            Array.Fill(EncryptionKey, (byte) 1);
        }

        public static byte[] StreamToBytes(Stream stream)
        {
            MemoryStream ms = new();
            stream.Position = 0; //reset due to other readers
            stream.CopyToAsync(ms).GetAwaiter().GetResult();
            return ms.ToArray();
        }

        public static async Task<DriveFileId> AddFile(DriveService driveService, Guid driveId, TestFileProps testFileProps)
        {
            var file = driveService.CreateFileId(driveId);

            var keyHeader = KeyHeader.NewRandom16();
            var key = EncryptionKey.ToSensitiveByteArray();
            var ekh = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, InitializationVector, ref key);
            await driveService.WriteEncryptedKeyHeader(file, ekh);

            var metadata = new FileMetadata(file)
            {
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                ContentType = testFileProps.PayloadContentType,
                AppData = new AppFileMetaData()
                {
                    Tags = new List<Guid>() {testFileProps.CategoryId.GetValueOrDefault()},
                    ContentIsComplete = testFileProps.ContentIsComplete,
                    JsonContent = JsonConvert.SerializeObject(testFileProps.MetadataJsonContent)
                }
            };

            await driveService.WriteMetaData(file, metadata);

            var payloadCipherStream = keyHeader.GetEncryptedStreamAes(testFileProps.PayloadData);
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
            CollectionAssert.AreEquivalent(metadata.AppData.Tags, storedMetadata.AppData.Tags);
            Assert.IsTrue(metadata.AppData.ContentIsComplete == storedMetadata.AppData.ContentIsComplete);
            Assert.IsTrue(metadata.AppData.JsonContent == storedMetadata.AppData.JsonContent);

            await using var storedPayloadStream = await driveService.GetPayloadStream(file);
            var storedPayloadBytes = storedPayloadStream.ToByteArray();;
            storedPayloadStream.Close();
            
            var payloadCipherBytes = payloadCipherStream.ToByteArray();;
            payloadCipherStream.Close();
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(payloadCipherBytes, storedPayloadBytes));

            return file;
        }
    }
}