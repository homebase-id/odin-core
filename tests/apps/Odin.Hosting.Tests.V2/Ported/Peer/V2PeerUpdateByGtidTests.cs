using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Apps;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// <c>PATCH /api/v2/peer/{odinId}/drives/{driveId}/files/by-gtid/{gtid}</c>: updating a file on another identity with
/// no local copy, the V2 twin of V1's <c>PATCH drive/files/update</c> with locale Peer. V1's route takes owner, app and
/// guest, and the real gate on both is <c>UseTransitWrite</c> when the update is sent.
///
/// Layout: Pippin sends and updates; Frodo hosts the drive, and grants Pippin Write through a circle.
/// </summary>
[TestFixture]
public class V2PeerUpdateByGtidTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Pippin];

    public interface IPeerUpdate
    {
        [Multipart]
        [Patch(UnifiedApiRouteConstants.PeerByGtid)]
        Task<ApiResponse<UpdateFileResult>> Update(string odinId, [AliasAs("driveId:guid")] Guid driveId,
            [AliasAs("gtid:guid")] Guid gtid, [AliasAs("streamdata")] StreamPart[] parts);
    }

    [Test]
    public async Task Owner_UpdatesTheRecipientsFile_HeaderAndPayloads()
    {
        var (pippin, frodo, drive, sent, payloadToDelete, payloadToKeep) = await SendFileAsync();

        var metadata = UpdatedMetadata();
        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();

        var response = await UpdateAsync(pippin, frodo.Identity, drive, sent, metadata, Instructions(drive, new UploadManifest
        {
            PayloadDescriptors =
            [
                new UploadManifestPayloadDescriptor
                {
                    PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                    Iv = Guid.Empty.ToByteArray(),
                    PayloadKey = payloadToAdd.Key,
                    ContentType = payloadToAdd.ContentType,
                    Thumbnails = new List<UploadedManifestThumbnailDescriptor>(),
                },
                new UploadManifestPayloadDescriptor
                {
                    PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                    PayloadKey = payloadToDelete.Key
                }
            ]
        }), [payloadToAdd]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.FileId, Is.EqualTo(Guid.Empty), "the recipient's file id isn't the sender's to know");
        Assert.That(response.Content.DriveId, Is.EqualTo(Guid.Empty), "there is no local file");
        Assert.That(response.Content.GlobalTransitId, Is.EqualTo(sent));
        Assert.That(response.Content.Updated, Is.Null, "there is no local file");
        Assert.That(response.Content.RecipientStatus.Keys, Is.EquivalentTo(new[] { frodo.Identity.DomainName }));
        Assert.That(response.Content.NewVersionTag, Is.Not.EqualTo(Guid.Empty));

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);

        var header = await HeaderAsync(frodo, drive, sent);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(metadata.AppData.DataType));
        Assert.That(header.FileMetadata.VersionTag, Is.EqualTo(response.Content.NewVersionTag));
        Assert.That(header.FileMetadata.Payloads.Select(p => p.Key),
            Is.EquivalentTo(new[] { payloadToKeep.Key, payloadToAdd.Key }));
    }

    /// <summary>A body naming someone else, some other file or a local update changes nothing: the path says.</summary>
    [Test]
    public async Task ThePathWinsOverTheInstructions()
    {
        var (pippin, frodo, drive, sent, _, _) = await SendFileAsync();
        var metadata = UpdatedMetadata();

        var response = await UpdateAsync(pippin, frodo.Identity, drive, sent, metadata, new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = new FileIdentifier
            {
                FileId = Guid.NewGuid(),
                TargetDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = drive.Type }
            },
            Recipients = [(Odin.Core.Identity.OdinId)Identities.Sam],
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.RecipientStatus.Keys, Is.EquivalentTo(new[] { frodo.Identity.DomainName }));

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);

        var header = await HeaderAsync(frodo, drive, sent);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content));
    }

    [Test]
    public async Task AppWithUseTransitWrite_UpdatesTheRecipientsFile()
    {
        var (pippin, frodo, drive, sent, _, _) = await SendFileAsync();
        var localDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(localDrive, "local drive");
        var app = await AppSession.SetupAsync(pippin, localDrive, DrivePermission.ReadWrite, [PermissionKeys.UseTransitWrite]);
        var metadata = UpdatedMetadata();

        var response = await UpdateAsync(app, frodo.Identity, drive, sent, metadata,
            Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content!.RecipientStatus[frodo.Identity.DomainName].ToString(), Is.EqualTo("Enqueued"));

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);

        var header = await HeaderAsync(frodo, drive, sent);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content));
    }

    [Test]
    public async Task AppWithoutUseTransitWrite_IsRefused()
    {
        var (pippin, frodo, drive, sent, _, _) = await SendFileAsync();
        var localDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(localDrive, "local drive");
        var app = await AppSession.SetupAsync(pippin, localDrive, DrivePermission.ReadWrite, [PermissionKeys.UseTransitRead]);

        var response = await UpdateAsync(app, frodo.Identity, drive, sent, UpdatedMetadata(),
            Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>A guest gets past the route, as on V1, and no further: it holds no <c>UseTransitWrite</c>.</summary>
    [Test]
    public async Task GuestWithoutUseTransitWrite_IsRefused()
    {
        var (pippin, frodo, drive, sent, _, _) = await SendFileAsync();
        var guest = await SetupCaller(CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), Identities.Pippin);

        var response = await UpdateAsync(guest, frodo.Identity, drive, sent, UpdatedMetadata(),
            Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Anonymous_IsRefused()
    {
        var (_, frodo, drive, sent, _, _) = await SendFileAsync();
        var anonymous = Host.CreateAnonymousClient(Identities.Pippin);

        var response = await anonymous.PatchAsync(
            $"/api/v2/peer/{frodo.Identity.DomainName}/drives/{(Guid)drive.Alias}/files/by-gtid/{sent}",
            new System.Net.Http.MultipartFormDataContent());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>The remote finds the drive by alias, which the path has, so instructions naming no file still reach it.</summary>
    [Test]
    public async Task InstructionsWithNoFile_StillUpdateTheRecipientsFile()
    {
        var (pippin, frodo, drive, sent, _, _) = await SendFileAsync();
        var metadata = UpdatedMetadata();

        var response = await UpdateAsync(pippin, frodo.Identity, drive, sent, metadata, new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);

        var header = await HeaderAsync(frodo, drive, sent);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content));
    }

    [Test]
    public async Task UpdatingOnYourself_IsABadRequest()
    {
        var (pippin, _, drive, sent, _, _) = await SendFileAsync();

        var response = await UpdateAsync(pippin, pippin.Identity, drive, sent, UpdatedMetadata(),
            Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PeerUpdateWithoutAllowDistribution_IsABadRequest()
    {
        var (pippin, frodo, drive, sent, _, _) = await SendFileAsync();
        var metadata = UpdatedMetadata();
        metadata.AllowDistribution = false;

        var response = await UpdateAsync(pippin, frodo.Identity, drive, sent, metadata,
            Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    //

    private static UploadFileMetadata UpdatedMetadata()
    {
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AllowDistribution = true;
        metadata.AccessControlList = AccessControlList.Connected;
        metadata.AppData.Content = "some new content here";
        metadata.AppData.DataType = 777;
        return metadata;
    }

    internal static FileUpdateInstructionSet Instructions(TargetDrive drive, UploadManifest manifest) => new()
    {
        Locale = UpdateLocale.Peer,
        TransferIv = ByteArrayUtil.GetRndByteArray(16),
        File = new FileIdentifier { TargetDrive = drive, GlobalTransitId = Guid.NewGuid() }, // the path's id is used
        Manifest = manifest
    };

    private async Task<(OwnerSession Pippin, OwnerSession Frodo, TargetDrive Drive, Guid Gtid,
        TestPayloadDefinition First, TestPayloadDefinition Second)> SendFileAsync()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await CollabChannelFlow.SetupAsync(frodo, drive, [pippin], driveName: "Frodo's drive");

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AllowDistribution = true;
        metadata.AppData.DataType = 555;
        metadata.AccessControlList = AccessControlList.Connected;

        var first = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var second = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();
        var payloads = new List<TestPayloadDefinition> { first, second };

        var response = await pippin.V1.PeerDirect.TransferNewFile(drive, metadata, [frodo.Identity], null,
            new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() }, payloads);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await pippin.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive, int.MaxValue);

        return (pippin, frodo, drive, response.Content!.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId, first, second);
    }

    internal static async Task<ApiResponse<UpdateFileResult>> UpdateAsync(IV2Caller caller, Odin.Core.Identity.OdinId recipient,
        TargetDrive drive, Guid gtid, UploadFileMetadata metadata, FileUpdateInstructionSet instructions,
        List<TestPayloadDefinition> payloads = null, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret, fileSystemType);

        var descriptor = new UpdateFileDescriptor { EncryptedKeyHeader = null, FileMetadata = metadata };
        List<StreamPart> parts =
        [
            new(new MemoryStream(OdinSystemSerializer.Serialize(instructions).ToUtf8ByteArray()), "instructionSet.encrypted",
                "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
            new(TestUtils.JsonEncryptAes(descriptor, instructions.TransferIv, ref sharedSecret), "fileDescriptor.encrypted",
                "application/json", Enum.GetName(MultipartUploadParts.Metadata))
        ];

        parts.AddRange((payloads ?? []).Select(p => new StreamPart(new MemoryStream(p.Content), p.Key, p.ContentType,
            Enum.GetName(MultipartUploadParts.Payload))));

        return await RestService.For<IPeerUpdate>(client).Update(recipient.DomainName, drive.Alias, gtid, parts.ToArray());
    }

    private static async Task<SharedSecretEncryptedFileHeader> HeaderAsync(OwnerSession owner, TargetDrive drive, Guid gtid)
    {
        var response = await owner.V1.Drive.QueryByGlobalTransitId(new GlobalTransitIdFileIdentifier
        {
            GlobalTransitId = gtid,
            TargetDrive = drive
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content!.SearchResults.Single();
    }
}
