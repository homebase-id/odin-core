using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// The two reads and the one write the transit fixtures share: find a file by its global transit id,
/// and put an unencrypted post on a channel drive.
/// </summary>
/// <remarks>
/// It replaces a V1 client method that has no <c>_Universal</c> twin.
/// <see cref="QueryByGlobalTransitIdAsync"/> stands in for
/// <c>DriveApiClient.QueryBatch(FileSystemType, FileQueryParamsV1)</c>, keeping its result options
/// (<c>MaxRecords = 10</c>, <c>IncludeMetadataHeader = true</c>) so a "there is exactly one" assertion
/// still means what it did — <c>UniversalDriveApiClient.QueryByGlobalTransitId</c> caps at one record,
/// which would turn that assertion into a tautology.
/// <para>
/// The transfer-history assertion that used to live here is
/// <see cref="DriveAsserts.AssertTransferStatus"/>, shared with the Peer and Connections fixtures.
/// </para>
/// </remarks>
internal static class TransitScenario
{
    /// <summary>
    /// The post the two reaction fixtures react to: one unencrypted metadata-only file on
    /// <paramref name="targetDrive"/>.
    /// </summary>
    /// <remarks>
    /// The originals carried a copy each, under the same name; the connected-reactions copy was the
    /// authenticated one with its two parameters fixed at their defaults. The general form is kept —
    /// <paramref name="acl"/> is the one thing the two fixtures differ on (anonymous on an anonymous
    /// drive, connected on a private channel).
    /// </remarks>
    public static async Task<UploadResult> UploadUnencryptedContentToChannelAsync(
        OwnerSession owner,
        TargetDrive targetDrive,
        string uploadedContent,
        bool allowDistribution = true,
        AccessControlList acl = null)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = allowDistribution,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = acl ?? AccessControlList.Connected
        };

        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata, FileSystemType.Standard);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content;
    }

    /// <summary>Everything on <paramref name="owner"/>'s drive carrying <paramref name="file"/>'s global transit id.</summary>
    public static async Task<List<SharedSecretEncryptedFileHeader>> QueryByGlobalTransitIdAsync(
        OwnerSession owner,
        GlobalTransitIdFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var response = await owner.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = file.TargetDrive,
                GlobalTransitId = [file.GlobalTransitId]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        }, fileSystemType);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return [.. response.Content!.SearchResults];
    }

    /// <summary>
    /// The one file on <paramref name="owner"/>'s drive carrying <paramref name="file"/>'s global
    /// transit id — asserts there is exactly one.
    /// </summary>
    public static async Task<SharedSecretEncryptedFileHeader> SingleByGlobalTransitIdAsync(
        OwnerSession owner,
        GlobalTransitIdFileIdentifier file,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var searchResults = await QueryByGlobalTransitIdAsync(owner, file, fileSystemType);
        Assert.That(searchResults.Count, Is.EqualTo(1));
        return searchResults[0];
    }
}
