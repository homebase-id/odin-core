using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Drive;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Tests.AppAPI.Drive;
using Youverse.Hosting.Tests.AppAPI.Transit;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatServerContext
{
    private TestAppContext _appContext;

    public ChatServerContext(TestAppContext appContext, WebScaffold scaffold)
    {
        Scaffold = scaffold;
        _appContext = appContext;
    }

    public WebScaffold Scaffold { get; }

    public string Sender => this._appContext.Identity;

    public async Task<(IEnumerable<T> items, string cursorState)> QueryBatch<T>(FileQueryParams queryParams, string cursorState)
    {
        await this.ProcessIncomingInstructions();

        queryParams.TargetDrive = _appContext.TargetDrive;

        using (var client = this.Scaffold.AppApi.CreateAppApiHttpClient(this._appContext.Identity, _appContext.ClientAuthenticationToken))
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

    /// <summary>
    /// Returns the results as a dictionary with the server FileId as key and T as the value
    /// </summary>
    /// <returns></returns>
    public async Task<(IDictionary<Guid, T> dictiionary, string cursorState)> QueryBatchDictionary<T>(FileQueryParams queryParams, string cursorState)
    {
        await this.ProcessIncomingInstructions();

        queryParams.TargetDrive = _appContext.TargetDrive;

        using (var client = this.Scaffold.AppApi.CreateAppApiHttpClient(this._appContext.Identity, _appContext.ClientAuthenticationToken))
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

            //Note: intentionally left out decryption for this prototype
            var items = batch.SearchResults.ToDictionary(
                item => item.FileId,
                item => DotYouSystemSerializer.Deserialize<T>(item.FileMetadata.AppData.JsonContent));

            return (items, batch.CursorState);
        }
    }

    public async Task<QueryBatchResponse> QueryBatch(FileQueryParams queryParams, string cursorState)
    {
        await this.ProcessIncomingInstructions();

        queryParams.TargetDrive = _appContext.TargetDrive;

        using (var client = this.Scaffold.AppApi.CreateAppApiHttpClient(_appContext.Identity, _appContext.ClientAuthenticationToken))
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
            return batch;
        }
    }

    public async Task ProcessIncomingInstructions(int delaySeconds = 0)
    {
        Task.Delay(delaySeconds).Wait();
        using (var rClient = Scaffold.AppApi.CreateAppApiHttpClient(_appContext.Identity, _appContext.ClientAuthenticationToken))
        {
            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
            var resp = await transitAppSvc.ProcessIncomingInstructions(new ProcessTransitInstructionRequest() { TargetDrive = _appContext.TargetDrive });
            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
        }
    }

    public async Task ProcessOutbox(int batchSize)
    {
        await Scaffold.OwnerApi.ProcessOutbox(_appContext.Identity, batchSize);
    }

    public async Task<UploadResult> SendFile(UploadFileMetadata fileMetadata, UploadInstructionSet instructionSet)
    {
        UploadResult transferResult = null;
        using (var client = this.Scaffold.AppApi.CreateAppApiHttpClient(_appContext.Identity, _appContext.ClientAuthenticationToken))
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
            transferResult = response.Content;

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

        
        await this.ProcessOutbox(instructionSet?.TransitOptions?.Recipients?.Count ?? 1);

        return transferResult;
    }

    public async Task UploadFile(UploadFileMetadata fileMetadata, UploadInstructionSet instructionSet)
    {
        if (instructionSet.TransitOptions?.Recipients?.Any() ?? false)
        {
            throw new Exception("You're trying to send a file, use the SendFile method or clear the recipients");
        }

        await this.SendFile(fileMetadata, instructionSet);
    }

    public void DeleteFile(ExternalFileIdentifier fileId)
    {
        this.Scaffold.AppApi.DeleteFile(_appContext, fileId).GetAwaiter().GetResult();
    }
}