using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.DriveApi.App;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatContext
{
    public DotYouIdentity Sender { get; }
    private TestSampleAppContext _appContext;
    private Dictionary<Guid, List<string>> _groups;

    public ChatContext(DotYouIdentity sender, TestSampleAppContext appContext, WebScaffold scaffold)
    {
        Sender = sender;
        Scaffold = scaffold;
        _appContext = appContext;
    }

    public WebScaffold Scaffold { get; }

    public async Task<(IEnumerable<T> items, string CursorState)> QueryBatch<T>(TestIdentity identity, FileQueryParams queryParams, string cursorState)
    {
        await this.ProcessIncomingTransfers();

        queryParams.TargetDrive = _appContext.TargetDrive;
        
        using (var client = this.Scaffold.AppApi.CreateAppApiHttpClient(identity.DotYouId, _appContext.ClientAuthenticationToken))
        {
            var svc = this.Scaffold.RestServiceFor<IDriveTestHttpClientForApps>(client, _appContext.SharedSecret);
            var request = new QueryBatchRequest()
            {
                QueryParams = queryParams,
                ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                {
                    CursorState = cursorState,
                    MaxRecords = 100,
                    IncludeMetadataHeader = true
                }
            };

            var response = await svc.QueryBatch(request);
            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var batch = response.Content!;

            //Note: intentionally left out decryption
            var items = batch.SearchResults.Select(item =>
                DotYouSystemSerializer.Deserialize<T>(item.FileMetadata.AppData.JsonContent));

            return (items, batch.CursorState);
        }
    }

    public async Task ProcessIncomingTransfers(int delaySeconds = 0)
    {
        Task.Delay(delaySeconds).Wait();
        using (var rClient = Scaffold.AppApi.CreateAppApiHttpClient(Sender, _appContext.ClientAuthenticationToken))
        {
            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
            var resp = await transitAppSvc.ProcessIncomingTransfers(new ProcessTransfersRequest() { TargetDrive = _appContext.TargetDrive });
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task ProcessOutbox()
    {
        await Scaffold.OwnerApi.ProcessOutbox(Sender);
    }

    public async Task SendFile(UploadFileMetadata fileMetadata, UploadInstructionSet instructionSet)
    {
        using (var client = this.Scaffold.AppApi.CreateAppApiHttpClient(Sender, _appContext.ClientAuthenticationToken))
        {
            var keyHeader = KeyHeader.NewRandom16();
            var transferIv = instructionSet.TransferIv;

            var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var sharedSecret = _appContext.SharedSecret.ToSensitiveByteArray();

            fileMetadata.PayloadIsEncrypted = true;
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);
            var payloadCipher = new MemoryStream();

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

                foreach (var recipient in instructionSet.TransitOptions.Recipients)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                    Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                }
            }

            keyHeader.AesKey.Wipe();
        }

        await this.ProcessOutbox();
    }
}