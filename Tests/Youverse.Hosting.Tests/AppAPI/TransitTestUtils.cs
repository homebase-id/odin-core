using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Tests.AppAPI.Drive;
using Youverse.Hosting.Tests.AppAPI.Transit;

namespace Youverse.Hosting.Tests.AppAPI
{
    public class TransitTestUtilsOptions
    {
        public static TransitTestUtilsOptions Default = new TransitTestUtilsOptions()
        {
            ProcessOutbox = false
        };

        /// <summary>
        /// Indicates if the process outbox endpoint should be called after sending a transfer
        /// </summary>
        public bool ProcessOutbox { get; set; }
    }

    public class TestSampleAppContext
    {
        public Guid AppId { get; set; }
        public DotYouAuthenticationResult AuthResult { get; set; }
        public byte[] AppSharedSecretKey { get; set; }
    }

    /// <summary>
    /// Data returned when using the TransitTestUtils
    /// </summary>
    public class TransitTestUtilsContext : UploadTestUtilsContext
    {
        public Dictionary<DotYouIdentity, TestSampleAppContext> RecipientContexts { get; set; }
    }

    public class UploadTestUtilsContext
    {
        public Guid AppId { get; set; }
        public byte[] DeviceUid { get; set; }
        public DotYouAuthenticationResult AuthResult { get; set; }
        public byte[] AppSharedSecretKey { get; set; }

        /// <summary>
        /// The instruction set that was uploaded
        /// </summary>
        public UploadInstructionSet InstructionSet { get; set; }

        /// <summary>
        /// The file meta data that was uploaded. 
        /// </summary>
        public UploadFileMetadata FileMetadata { get; set; }
    }

    public static class TransitTestUtils
    {
        public static async Task<UploadTestUtilsContext> Upload(TestScaffold scaffold, DotYouIdentity identity, TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    DriveId = null,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },
                TransitOptions = null
            };

            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AppData = new()
                {
                    CategoryId = Guid.Empty,
                    ContentIsComplete = true,
                    JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" })
                }
            };


            return (UploadTestUtilsContext)await TransferFile(scaffold, identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        public static async Task<UploadTestUtilsContext> Upload(TestScaffold scaffold, DotYouIdentity identity, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    DriveId = null,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },
                TransitOptions = null
            };

            return (UploadTestUtilsContext)await TransferFile(scaffold, identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        /// <summary>
        /// Transfers a file using default file metadata
        /// </summary>
        /// <returns></returns>
        public static async Task<TransitTestUtilsContext> TransferFile(TestScaffold scaffold, DotYouIdentity sender, List<string> recipients, TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    DriveId = null,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = recipients
                }
            };

            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AppData = new()
                {
                    CategoryId = Guid.Empty,
                    ContentIsComplete = true,
                    JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" })
                }
            };

            return await TransferFile(scaffold, sender, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        public static async Task<TransitTestUtilsContext> TransferFile(TestScaffold scaffold, DotYouIdentity identity, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            var testContext = await scaffold.SetupTestSampleApp(identity);

            //Setup the app on all recipient DIs
            var recipientContexts = new Dictionary<DotYouIdentity, TestSampleAppContext>();
            foreach (var r in instructionSet.TransitOptions?.Recipients ?? new List<string>())
            {
                var recipient = (DotYouIdentity)r;
                var ctx = await scaffold.SetupTestSampleApp(testContext.AppId, recipient);
                recipientContexts.Add(recipient, ctx);
            }

            var keyHeader = KeyHeader.NewRandom16();
            var transferIv = instructionSet.TransferIv;

            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, testContext.AppSharedSecretKey),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = Utils.JsonEncryptAes(descriptor, transferIv, testContext.AppSharedSecretKey);

            var payloadData = "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

            using (var client = scaffold.CreateAppApiHttpClient(identity, testContext.AuthResult))
            {
                var transitSvc = RestService.For<ITransitTestHttpClient>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.DriveId, Is.Not.EqualTo(Guid.Empty));

                if (instructionSet.TransitOptions?.Recipients != null)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count, "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                    }
                }

                if (options is { ProcessOutbox: true })
                {
                    await transitSvc.ProcessOutbox();
                }
            }

            keyHeader.AesKey.Wipe();

            return new TransitTestUtilsContext()
            {
                AppId = testContext.AppId,
                AuthResult = testContext.AuthResult,
                AppSharedSecretKey = testContext.AppSharedSecretKey,
                InstructionSet = instructionSet,
                FileMetadata = fileMetadata,
                RecipientContexts = recipientContexts
            };
        }
    }
}