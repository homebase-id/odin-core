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
/// The original declared a fourth case source, <c>WhenGuestOnlyHasReadAccess()</c>, a read-only guest
/// expecting <see cref="HttpStatusCode.Forbidden"/>, that no <c>[TestCaseSource]</c> referenced, so it
/// never ran (#1767 #8). It is wired up here as <see cref="ReadOnlyGuestOversizedCases"/> and
/// <see cref="ReadOnlyGuestCases"/>: a caller without write access is refused for that, whatever the
/// size of what it sends.
///
/// The original's write rows carried an <c>expected</c> status code that no test body read; every
/// one asserted <c>BadRequest</c> outright. The column is back, now that the read-only guest rows
/// expect something else.
///
/// The two oversized-field uploads were one test method each in the original; here they are two rows
/// of <see cref="FailWhenAppDataIsTooLarge"/>, which differ only in which field is
/// overlong.
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
    public static IEnumerable<CallerSpec> WriteCases()
    {
        yield return CallerSpec.Owner(DriveSpec.Anon());
        yield return CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write);
        yield return CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write);
    }

    /// <summary>Which oversized field the upload carries — the only difference between the two rows.</summary>
    public enum OversizedField
    {
        AppDataContent,
        PreviewThumbnail
    }

    public static IEnumerable<object[]> OversizedWriteCases()
    {
        foreach (var spec in WriteCases())
        {
            yield return [spec, OversizedField.AppDataContent, HttpStatusCode.BadRequest];
            yield return [spec, OversizedField.PreviewThumbnail, HttpStatusCode.BadRequest];
        }
    }

    /// <summary>A guest with only read access to the drive. It cannot write, so it is refused for that.</summary>
    public static IEnumerable<CallerSpec> ReadOnlyGuestCases()
    {
        yield return CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read);
    }

    public static IEnumerable<object[]> ReadOnlyGuestOversizedCases()
    {
        foreach (var spec in ReadOnlyGuestCases())
        {
            yield return [spec, OversizedField.AppDataContent, HttpStatusCode.Forbidden];
            yield return [spec, OversizedField.PreviewThumbnail, HttpStatusCode.Forbidden];
        }
    }

    [Test, TestCaseSource(nameof(OversizedWriteCases)), TestCaseSource(nameof(ReadOnlyGuestOversizedCases))]
    public async Task FailWhenAppDataIsTooLarge(CallerSpec spec, OversizedField oversized, HttpStatusCode expected)
    {
        // Setup
        var caller = await SetupCaller(spec);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        if (oversized == OversizedField.AppDataContent)
        {
            uploadedFileMetadata.AppData.Content = new string(Enumerable.Repeat('A', AppFileMetaData.MaxAppDataContentLength + 1).ToArray());
        }
        else
        {
            uploadedFileMetadata.AppData.Content = "123";
            uploadedFileMetadata.AppData.PreviewThumbnail = new ThumbnailContent
            {
                PixelWidth = 100,
                PixelHeight = 100,
                ContentType = "image/png",
                Content = Enumerable.Repeat((byte)'A', ThumbnailContent.MaxTinyThumbLength + 1).ToArray()
            };
        }

        // Act
        var callerDriveClient = caller.V1.Drive;
        var response = await callerDriveClient.UploadNewMetadata(spec.TargetDrive, uploadedFileMetadata);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    public static IEnumerable<object[]> MetadataUpdateCases()
    {
        foreach (var spec in WriteCases())
        {
            yield return [spec, HttpStatusCode.BadRequest];
        }

        foreach (var spec in ReadOnlyGuestCases())
        {
            yield return [spec, HttpStatusCode.Forbidden];
        }
    }

    [Test, TestCaseSource(nameof(MetadataUpdateCases))]
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
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Act

        var uploadResult = response.Content;
        var getHeaderResponse1 = await ownerDriveClient.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
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

        Assert.That(updateResponse.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task FailWithBadRequestWithIdentityOtherThanOriginalAuthorUpdatesAFileUsingUpdateBatch()
    {
        var (caller, originalAuthorOwner) = await SetupCallerWithOwner(CallerSpec.Owner(DriveSpec.Anon()), Identities.Pippin);

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
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

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

        uploadedFileMetadata.AppData.Content = new string(Enumerable.Repeat('A', AppFileMetaData.MaxAppDataContentLength + 1).ToArray());
        uploadedFileMetadata.AppData.DataType = 222;

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

        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, uploadedFileMetadata, [payloadToAdd]);
        Assert.That(updateFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
