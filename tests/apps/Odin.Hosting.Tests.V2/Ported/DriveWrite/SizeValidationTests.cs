using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDriveSizeValidationTests</c>. Every way an oversized piece
/// of app data can reach the server must be refused with <c>400</c> rather than stored: on a new
/// upload (<c>AppData.Content</c> and the preview thumbnail), on an encrypted metadata-only
/// overwrite, and on an update-batch sent over peer into a collaboration channel.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> drive endpoints through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>caller.V1.Drive</c> / <c>owner.V1.Drive</c>.
///
/// <b>Carried defect — an assertion that has never run.</b> The original declares a fourth case
/// source, <c>WhenGuestOnlyHasReadAccess()</c>, yielding a read-only guest expecting
/// <see cref="HttpStatusCode.Forbidden"/>. No <c>[TestCaseSource]</c> anywhere references it, so that
/// Forbidden path has never executed. It is carried here as
/// <see cref="GuestReadOnlyCases"/> — still unreferenced, still dead — so the port is a move rather
/// than a fix. Wiring it up is a behaviour change and belongs in its own change.
///
/// Also note that the <c>expected</c> parameter is never read by any of these tests: every row asserts
/// <c>BadRequest</c> outright, because validation precedes authorization on all four endpoints, so the
/// matrix rows differ only in who sends the request. That too is carried verbatim.
///
/// The peer test's trailing disconnect / unfollow block is dropped: it only restored state, which
/// <see cref="V2Fixture"/>'s per-test reset already guarantees. The peer test also now creates the
/// (unused) drive its <see cref="CallerSpec"/> names, because
/// <see cref="V2Fixture.SetupCallerWithOwner"/> creates it as part of building the caller; the original
/// never created it. Checked: nothing reads that drive, so the extra create is inert.
/// </remarks>
[TestFixture]
public class SizeValidationTests : V2Fixture
{
    /// <summary>
    /// Pippin first: he is the identity the original acted as in every test. The other three are the
    /// peer test's cast — Frodo owns the collaboration channel, Collab is the secondary author, and
    /// Tom Bombadil is the following member.
    /// </summary>
    protected override string[] HostIdentities =>
        [Identities.Pippin, Identities.Collab, Identities.Frodo, Identities.TomBombadil];

    /// <summary>
    /// The original's three stacked owner/app/guest case sources, inline. Every row expects the
    /// endpoint to be reached; the refusal under test is the size check, not the drive grant.
    /// </summary>
    public static IEnumerable<object[]> WriteCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
    }

    /// <summary>
    /// Carried from the original verbatim, including the fact that nothing consumes it. See the
    /// fixture's remarks: this Forbidden path has never run.
    /// </summary>
    public static IEnumerable<object[]> GuestReadOnlyCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
    }

    public static IEnumerable<object[]> OwnerOnlyCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task FailWithBadRequestWhenAppDataContentIsTooLarge(CallerSpec spec, HttpStatusCode expected)
    {
        // Setup
        var caller = await SetupCaller(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = new string(Enumerable.Repeat('A', AppFileMetaData.MaxAppDataContentLength + 1).ToArray());

        // Act
        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task FailWithBadRequestWhenPreviewThumbnailIsTooLarge(CallerSpec spec, HttpStatusCode expected)
    {
        // Setup
        var caller = await SetupCaller(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "123";
        uploadedFileMetadata.AppData.PreviewThumbnail = new ThumbnailContent
        {
            PixelWidth = 100,
            PixelHeight = 100,
            ContentType = "image/png",
            Content = Enumerable.Repeat((byte)'A', ThumbnailContent.MaxTinyThumbLength + 1).ToArray()
        };

        // Act
        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanUpdateEncryptedMetadataWithStorageIntent_MetadataOnly(CallerSpec spec, HttpStatusCode expected)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        const string originalContent = "some content here";
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AppData.Content = originalContent;

        var originalKeyHeader = KeyHeader.NewRandom16();
        var (response, _) = await ownerDriveClient.UploadNewEncryptedMetadata(spec.TargetDrive, uploadedFileMetadata, originalKeyHeader);
        Assert.That(response.IsSuccessStatusCode, Is.True);

        // Act

        var uploadResult = response.Content;
        var getHeaderResponse1 = await ownerDriveClient.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse1.IsSuccessStatusCode, Is.True);
        var uploadedFile1 = getHeaderResponse1.Content;

        var callerDriveClient = caller.V1.Drive;

        var updatedMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        updatedMetadata.AppData.Content = new string(Enumerable.Repeat('A', AppFileMetaData.MaxAppDataContentLength + 1).ToArray());
        updatedMetadata.VersionTag = uploadedFile1.FileMetadata.VersionTag;
        updatedMetadata.IsEncrypted = true;

        var newKeyHeader = new KeyHeader()
        {
            Iv = ByteArrayUtil.GetRndByteArray(16),
            AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
        };

        var (updateResponse, _) = await callerDriveClient
            .UpdateExistingEncryptedMetadata(uploadResult.File, newKeyHeader, updatedMetadata);

        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test, TestCaseSource(nameof(OwnerOnlyCases))]
    public async Task FailWithBadRequestWithIdentityOtherThanOriginalAuthorUpdatesAFileUsingUpdateBatch(
        CallerSpec spec,
        HttpStatusCode expected)
    {
        var (caller, originalAuthorOwner) = await SetupCallerWithOwner(spec, Identities.Pippin);

        var secondaryAuthorOwner = await LoginAsOwner(Identities.Collab);
        var collabChannelOwner = await LoginAsOwner(Identities.Frodo);
        var member2Owner = await LoginAsOwner(Identities.TomBombadil);

        await collabChannelOwner.Admin.DisableAutoAcceptIntroductions();
        await originalAuthorOwner.Admin.DisableAutoAcceptIntroductions();
        await member2Owner.Admin.DisableAutoAcceptIntroductions();
        await secondaryAuthorOwner.Admin.DisableAutoAcceptIntroductions();

        var originalAuthor = originalAuthorOwner.Identity;
        var collabChannel = collabChannelOwner.Identity;
        var secondaryAuthor = secondaryAuthorOwner.Identity;
        var member2 = member2Owner.Identity;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await collabChannelOwner.Admin.CreateDrive(collabChannelDrive, "Test channel drive 001",
            allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } });

        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannelOwner.Admin.CreateCircle(collabChannelId, "circle with some access", permissions);

        await originalAuthorOwner.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwner.Connections.AcceptConnectionRequest(originalAuthor, [collabChannelId]);

        await secondaryAuthorOwner.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwner.Connections.AcceptConnectionRequest(secondaryAuthor, [collabChannelId]);

        await member2Owner.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwner.Connections.AcceptConnectionRequest(member2, [collabChannelId]);
        await member2Owner.V1.Follower.FollowIdentity(collabChannel, FollowerNotificationType.AllNotifications);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.DataType = 111;
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AccessControlList = AccessControlList.Connected;
        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payload1,
            payload2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        //Pippin sends a file to the recipient
        var response = await originalAuthorOwner.V1.PeerDirect.TransferNewFile(collabChannelDrive, uploadedFileMetadata,
            [collabChannel], null,
            uploadManifest,
            testPayloads);
        await originalAuthorOwner.Sync.DrainOutboxAsync();
        Assert.That(response.IsSuccessStatusCode, Is.True);

        // wait for the collab channel to distribute feed
        await collabChannelOwner.Sync.ProcessInboxAsync(collabChannelDrive);
        await collabChannelOwner.Sync.DrainOutboxAsync();

        //
        // Update the file via pippin's identity
        //

        await originalAuthorOwner.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await secondaryAuthorOwner.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);
        await member2Owner.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        var callerDriveClient = caller.V1.Drive;

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = new string(Enumerable.Repeat('A', AppFileMetaData.MaxAppDataContentLength + 1).ToArray());
        updatedFileMetadata.AppData.DataType = 222;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = remoteTargetFile,
            Recipients = [collabChannel],
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = Guid.Empty.ToByteArray(),
                        PayloadKey = payloadToAdd.Key,
                        DescriptorContent = null,
                        ContentType = payloadToAdd.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>(),
                    },
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = payload1.Key
                    }
                ]
            }
        };

        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
