using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// The parts the three <c>UpdateBatchWithRecipients*</c> ports share, which the originals carried as
/// a copy apiece: the recipient arrange, the refusal act, and the post-update assert sweep over the
/// sender and every recipient.
/// </summary>
/// <remarks>
/// The one thing that makes this scenario its own is the drive: it must carry
/// <see cref="BuiltInDriveAttributes.IsCollaborativeChannel"/> (<see cref="DriveSpec.Collab"/> /
/// <see cref="DriveSpec.CollabAttributes"/>). On a collaboration drive
/// <c>PeerFileUpdateWriter.DetermineAclAsync</c> keeps the sending identity's ACL on the recipient's
/// copy instead of narrowing it to owner-only, which is what makes an update fanned out to a peer
/// readable there.
/// </remarks>
internal static class UpdateBatchPeerScenario
{
    /// <summary>
    /// Logs each recipient in, gives it the same collaboration drive as the sender, and connects it
    /// to the sender with a circle granting the sender <see cref="DrivePermission.Write"/> on it.
    /// </summary>
    public static async Task<List<OwnerSession>> SetupRecipients(
        OdinHost host,
        OwnerSession sender,
        IEnumerable<string> recipientIdentities,
        TargetDrive drive)
    {
        var sessions = new List<OwnerSession>();
        foreach (var identity in recipientIdentities)
        {
            var recipient = await OwnerSession.LoginAsync(host, identity);
            await PeerFlow.CreatePeerDriveAsync(sender, recipient, DrivePermission.Write,
                drive: drive, attributes: DriveSpec.CollabAttributes);
            sessions.Add(recipient);
        }

        return sessions;
    }

    /// <summary>
    /// Act + assert for the caller-matrix rows that expect a refusal. Those rows never reach the
    /// peer half of the flow, so they skip the recipient arrange entirely: the recipients here are
    /// bare identity strings, with no sessions, drives or connections behind them.
    /// </summary>
    /// <remarks>
    /// The seed upload, however, is <b>not</b> optional, which is where this endpoint departs from
    /// the "don't seed for rows that early-return" rule in the fixture README. Measured against the
    /// running server: a Guest holding only Read is refused before the file is touched, but a Guest
    /// holding Write clears the drive check and is refused further in — with no file to update it
    /// answers 500 instead of 403. An update with no <c>VersionTag</c> answers 400 for every caller.
    /// So the row still needs a real file and its version tag; what it does not need is anything
    /// peer-side.
    /// </remarks>
    public static async Task AssertUpdateRefused(
        IV2Caller caller,
        OwnerSession sender,
        TargetDrive drive,
        IEnumerable<string> recipientIdentities,
        HttpStatusCode expected)
    {
        var seedMetadata = SampleMetadataData.Create(fileType: 100);
        seedMetadata.AllowDistribution = true;
        var seedResponse = await sender.V1.Drive.UploadNewMetadata(drive, seedMetadata);
        Assert.That(seedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        seedMetadata.VersionTag = seedResponse.Content!.NewVersionTag;

        var instructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = seedResponse.Content.File.ToFileIdentifier(),
            Recipients = recipientIdentities.Select(i => (OdinId)i).ToList(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        var response = await caller.V1.Drive.UpdateFile(instructionSet, seedMetadata, []);
        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    /// <summary>
    /// Delivers the update (<see cref="PeerFlow.DistributeAsync(OwnerSession, IEnumerable{OwnerSession}, TargetDrive)"/>),
    /// then asserts the sender's local copy and every recipient's copy carry the updated content,
    /// data type and version tag, and that neither has payloads left.
    /// </summary>
    /// <param name="deletedPayloadKey">
    /// When set, each recipient is additionally asked for that payload key and must 404 — the shape
    /// the "update deletes the seeded payload" cases assert.
    /// </param>
    public static async Task AssertUpdateLandedEverywhere(
        OwnerSession sender,
        IReadOnlyList<OwnerSession> recipients,
        TargetDrive drive,
        ExternalFileIdentifier targetFile,
        GlobalTransitIdFileIdentifier targetGlobalTransitIdFileIdentifier,
        UploadFileMetadata updatedFileMetadata,
        Guid expectedVersionTag,
        string deletedPayloadKey = null)
    {
        await PeerFlow.DistributeAsync(sender, recipients, drive);

        //
        // ensure the local file exists and is updated correctly
        //
        var getHeaderResponse = await sender.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.VersionTag, Is.EqualTo(expectedVersionTag));
        Assert.That(header.FileMetadata.Payloads, Is.Empty);

        // Ensure we find the file on the recipient
        //
        await DriveAsserts.AssertFileFoundByDataType(
            sender.V1.Drive, targetFile.TargetDrive, updatedFileMetadata.AppData.DataType, targetFile.FileId);

        // ensure the recipients get the file

        foreach (var recipient in recipients)
        {
            var recipientFileResponse = await recipient.V1.Drive.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
            var remoteFileHeader = recipientFileResponse.Content!.SearchResults.FirstOrDefault();

            Assert.That(remoteFileHeader, Is.Not.Null, $"recipient {recipient.Identity} should have the file");
            Assert.That(remoteFileHeader!.FileMetadata.AppData.Content, Is.EqualTo(updatedFileMetadata.AppData.Content));
            Assert.That(remoteFileHeader.FileMetadata.AppData.DataType, Is.EqualTo(updatedFileMetadata.AppData.DataType));
            Assert.That(remoteFileHeader.FileMetadata.VersionTag, Is.EqualTo(expectedVersionTag));
            Assert.That(remoteFileHeader.FileMetadata.Payloads, Is.Empty);

            if (deletedPayloadKey == null)
            {
                continue;
            }

            var getPayloadResponse = await recipient.V1.Drive.GetPayload(new ExternalFileIdentifier()
            {
                FileId = remoteFileHeader.FileId,
                TargetDrive = targetGlobalTransitIdFileIdentifier.TargetDrive
            }, deletedPayloadKey);

            Assert.That(getPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }
}
