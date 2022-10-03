using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.DriveApi.App;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.AppAPI.Utils
{
    public class AppApiTestUtils
    {
        private readonly OwnerApiTestUtils _ownerApi;

        public AppApiTestUtils(OwnerApiTestUtils ownerApi)
        {
            _ownerApi = ownerApi;
        }

        public HttpClient CreateAppApiHttpClient(TestIdentity identity, ClientAuthenticationToken token)
        {
            return this.CreateAppApiHttpClient(identity.DotYouId, token);
        }
        
        /// <summary>
        /// Creates a client for use with the app API (/api/apps/v1/...)
        /// </summary>
        public HttpClient CreateAppApiHttpClient(DotYouIdentity identity, ClientAuthenticationToken token)
        {
            var cookieJar = new CookieContainer();
            cookieJar.Add(new Cookie(AppAuthConstants.ClientAuthTokenCookieName, token.ToString(), null, identity));
            HttpMessageHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };

            HttpClient client = new(handler);
            client.Timeout = TimeSpan.FromMinutes(15);

            client.BaseAddress = new Uri($"https://{identity}");
            return client;
        }

        public HttpClient CreateAppApiHttpClient(TestSampleAppContext appTestContext)
        {
            return CreateAppApiHttpClient(appTestContext.Identity, appTestContext.ClientAuthenticationToken);
        }

        // public async Task<AppTransitTestUtilsContext> Upload(DotYouIdentity identity, TransitTestUtilsOptions options = null)
        // {
        //     var transferIv = ByteArrayUtil.GetRndByteArray(16);
        //
        //     var instructionSet = new UploadInstructionSet()
        //     {
        //         TransferIv = transferIv,
        //         StorageOptions = new StorageOptions()
        //         {
        //             Drive = TargetDrive.NewTargetDrive(),
        //             OverwriteFileId = null,
        //             ExpiresTimestamp = null
        //         },
        //         TransitOptions = null
        //     };
        //
        //     var fileMetadata = new UploadFileMetadata()
        //     {
        //         ContentType = "application/json",
        //         AppData = new()
        //         {
        //             ContentIsComplete = true,
        //             JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
        //         }
        //     };
        //
        //     return (AppTransitTestUtilsContext)await TransferFile(identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        // }

        public async Task<AppTransitTestUtilsContext> Upload(TestIdentity identity, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var targetDrive = new TargetDrive()
            {
                Alias = Guid.Parse("99888555-0000-0000-0000-000000004445"),
                Type = Guid.NewGuid()
            };

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = targetDrive,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },
                TransitOptions = null
            };

            return (AppTransitTestUtilsContext)await TransferFile(identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        private async Task<AppTransitTestUtilsContext> TransferFile(TestIdentity sender, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            if (options.ProcessTransitBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception("Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            Guid appId = Guid.NewGuid();
            var testAppContext = await _ownerApi.SetupTestSampleApp(appId, sender, false, instructionSet.StorageOptions.Drive, options.DriveAllowAnonymousReads);

            //Setup the app on all recipient DIs
            var recipientContexts = new Dictionary<DotYouIdentity, TestSampleAppContext>();
            foreach (var r in instructionSet.TransitOptions?.Recipients ?? new List<string>())
            {
                var recipient = TestIdentities.All[r];
                var ctx = await _ownerApi.SetupTestSampleApp(testAppContext.AppId, recipient, false, testAppContext.TargetDrive);
                recipientContexts.Add(recipient.DotYouId, ctx);

                await _ownerApi.CreateConnection(sender.DotYouId, recipient.DotYouId);
            }

            var payloadData = options?.PayloadData ?? "{payload:true, image:'b64 data'}";

            using (var client = this.CreateAppApiHttpClient(sender.DotYouId, testAppContext.ClientAuthenticationToken))
            {
                var keyHeader = KeyHeader.NewRandom16();
                var transferIv = instructionSet.TransferIv;

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                var sharedSecret = testAppContext.SharedSecret.ToSensitiveByteArray();

                fileMetadata.PayloadIsEncrypted = true;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

                payloadData = options?.PayloadData ?? payloadData;
                var payloadCipher = keyHeader.EncryptDataAesAsStream(payloadData);

                var transitSvc = RestService.For<IDriveTestHttpClientForApps>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

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
                    await _ownerApi.ProcessOutbox(sender.DotYouId);
                }

                if (options is { ProcessTransitBox: true })
                {
                    //wait for process outbox to run
                    Task.Delay(2000).Wait();

                    foreach (var rCtx in recipientContexts)
                    {
                        using (var rClient = this.CreateAppApiHttpClient(rCtx.Key, rCtx.Value.ClientAuthenticationToken))
                        {
                            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                            var resp = await transitAppSvc.ProcessIncomingTransfers(new ProcessTransfersRequest() { TargetDrive = rCtx.Value.TargetDrive });
                            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                        }
                    }
                }

                keyHeader.AesKey.Wipe();

                return new AppTransitTestUtilsContext()
                {
                    InstructionSet = instructionSet,
                    FileMetadata = fileMetadata,
                    RecipientContexts = recipientContexts,
                    PayloadData = payloadData,
                    TestAppContext = testAppContext,
                    UploadedFile = transferResult.File
                };
            }
        }
    }
}