using System;
using NUnit.Framework;
using NUnit.Framework.Legacy;
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
            ClassicAssert.IsNotNull(fileHeader.FileMetadata.GlobalTransitId);
        }

        ClassicAssert.IsTrue(fileHeader.FileState == FileState.Deleted);
        ClassicAssert.IsTrue(fileHeader.FileId != Guid.Empty);
        ClassicAssert.IsNotNull(fileHeader.ServerMetadata.AccessControlList);
        ClassicAssert.IsTrue(fileHeader.ServerMetadata.AccessControlList.RequiredSecurityGroup == expectedSecurityGroupType);
        ClassicAssert.IsTrue(fileHeader.FileMetadata.Updated > 0);
        ClassicAssert.IsTrue(fileHeader.FileMetadata.Created == default);
        ClassicAssert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.SenderOdinId));
        ClassicAssert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.OriginalAuthor));
        ClassicAssert.IsTrue(fileHeader.FileMetadata.IsEncrypted == default);

        ClassicAssert.IsNotNull(fileHeader.FileMetadata.AppData);
        ClassicAssert.IsTrue(fileHeader.FileMetadata.Payloads == default);
        ClassicAssert.IsTrue(fileHeader.FileMetadata.AppData.DataType == default);
        ClassicAssert.IsTrue(fileHeader.FileMetadata.AppData.FileType == default);
        ClassicAssert.IsTrue(fileHeader.FileMetadata.AppData.GroupId == default);
        ClassicAssert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.AppData.Content));
        ClassicAssert.IsTrue(fileHeader.FileMetadata.AppData.PreviewThumbnail == default);
        ClassicAssert.IsTrue(fileHeader.FileMetadata.AppData.UserDate == default);
        ClassicAssert.IsTrue(fileHeader.FileMetadata.AppData.Tags == default);
    }
}