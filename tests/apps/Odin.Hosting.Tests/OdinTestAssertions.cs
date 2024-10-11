using System;
using NUnit.Framework;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Tests;

/// <summary>
/// General assertions for various types.  See for yourself :)
/// </summary>
public static class OdinTestAssertions
{
    public static void FileHeaderIsMarkedDeleted(SharedSecretEncryptedFileHeader fileHeader, bool shouldHaveGlobalTransitId = false,
        SecurityGroupType expectedSecurityGroupType = SecurityGroupType.Owner)
    {
        if (shouldHaveGlobalTransitId)
        {
            Assert.IsNotNull(fileHeader.FileMetadata.GlobalTransitId);
        }

        Assert.IsTrue(fileHeader.FileState == FileState.Deleted);
        Assert.IsTrue(fileHeader.FileId != Guid.Empty);
        Assert.IsNotNull(fileHeader.ServerMetadata.AccessControlList);
        Assert.IsTrue(fileHeader.ServerMetadata.AccessControlList.RequiredSecurityGroup == expectedSecurityGroupType);
        Assert.IsTrue(fileHeader.FileMetadata.Updated > 0);
        Assert.IsTrue(fileHeader.FileMetadata.Created == default);
        Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.SenderOdinId));
        Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.OriginalAuthor));
        Assert.IsTrue(fileHeader.FileMetadata.IsEncrypted == default);

        Assert.IsNotNull(fileHeader.FileMetadata.AppData);
        Assert.IsTrue(fileHeader.FileMetadata.Payloads == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.DataType == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.FileType == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.GroupId == default);
        Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.AppData.Content));
        Assert.IsTrue(fileHeader.FileMetadata.AppData.PreviewThumbnail == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.UserDate == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.Tags == default);
    }
}