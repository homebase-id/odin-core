using System;
using Microsoft.AspNetCore.Connections;
using NUnit.Framework;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Core.Storage;

namespace Youverse.Hosting.Tests;

/// <summary>
/// General assertions for various types.  See for yourself :)
/// </summary>
public static class DotYouTestAssertions
{
    public static void FileHeaderIsMarkedDeleted(SharedSecretEncryptedFileHeader fileHeader, bool shouldHaveGlobalTransitId = false)
    {
        if(shouldHaveGlobalTransitId)
        {
            Assert.IsNotNull(fileHeader.FileMetadata.GlobalTransitId);
        }
        
        Assert.IsTrue(fileHeader.FileState == FileState.Deleted);
        Assert.IsTrue(fileHeader.FileId != Guid.Empty);
        Assert.IsNotNull(fileHeader.ServerMetadata.AccessControlList);
        Assert.IsTrue(fileHeader.ServerMetadata.AccessControlList.RequiredSecurityGroup == SecurityGroupType.Owner);
        Assert.IsTrue(fileHeader.FileMetadata.Updated > 0);
        Assert.IsTrue(fileHeader.FileMetadata.Created == default);
        Assert.IsTrue(fileHeader.FileMetadata.PayloadSize == default);
        Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.ContentType));
        Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.SenderDotYouId));
        Assert.IsTrue(fileHeader.FileMetadata.OriginalRecipientList == null);
        Assert.IsTrue(fileHeader.FileMetadata.PayloadIsEncrypted == default);

        Assert.IsNotNull(fileHeader.FileMetadata.AppData);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.ContentIsComplete == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.AdditionalThumbnails == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.DataType == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.FileType == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.GroupId == default);
        Assert.IsTrue(string.IsNullOrEmpty(fileHeader.FileMetadata.AppData.JsonContent));
        Assert.IsTrue(fileHeader.FileMetadata.AppData.PreviewThumbnail == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.UserDate == default);
        Assert.IsTrue(fileHeader.FileMetadata.AppData.Tags == default);
    }
}