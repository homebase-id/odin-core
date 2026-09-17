using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Reactions;

/// <summary>
/// The arrange the comment fixtures in this folder share: a channel drive, an unencrypted
/// owner-only post on it, and comments against that post.
/// </summary>
/// <remarks>
/// <see cref="ReactionTests"/> and <see cref="ReactionPreviewTests"/> each hand-built the same
/// <see cref="UploadFileMetadata"/> literal that <see cref="SampleMetadataData.CreateWithContent"/>
/// already produces — only <c>AllowDistribution</c>, which that factory leaves at its default, has
/// to be set here.
/// </remarks>
internal static class ChannelPosts
{
    /// <summary>A fresh channel drive with anonymous reads off — every original's "A Channel Drive".</summary>
    public static async Task<TargetDrive> CreateChannelDriveAsync(OwnerSession owner)
    {
        var drive = new TargetDrive
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await owner.Admin.CreateDrive(drive, "A Channel Drive", allowAnonymousReads: false);
        return drive;
    }

    /// <summary>One unencrypted, owner-only, distributable standard file carrying <paramref name="content"/>.</summary>
    public static Task<UploadResult> UploadPostAsync(OwnerSession owner, TargetDrive targetDrive, string content,
        int fileType = default)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType, content);
        metadata.AllowDistribution = true;
        return UploadAsync(owner, targetDrive, metadata, FileSystemType.Standard);
    }

    /// <summary>One unencrypted, owner-only, non-distributable comment against <paramref name="referencedFile"/>.</summary>
    public static Task<UploadResult> UploadCommentAsync(OwnerSession owner, TargetDrive targetDrive,
        GlobalTransitIdFileIdentifier referencedFile, string content)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType: default, content);
        metadata.ReferencedFile = referencedFile;
        return UploadAsync(owner, targetDrive, metadata, FileSystemType.Comment);
    }

    /// <summary>A comment whose metadata the test built itself.</summary>
    public static Task<UploadResult> UploadCommentAsync(OwnerSession owner, TargetDrive targetDrive,
        UploadFileMetadata metadata) =>
        UploadAsync(owner, targetDrive, metadata, FileSystemType.Comment);

    private static async Task<UploadResult> UploadAsync(OwnerSession owner, TargetDrive targetDrive,
        UploadFileMetadata metadata, FileSystemType fileSystemType)
    {
        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, metadata, fileSystemType);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content!;
    }
}
