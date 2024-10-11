using System;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests;

public static class SampleMetadataData
{
    public static UploadFileMetadata Create(int fileType, Guid? groupId = null, AccessControlList acl = null, bool allowDistribution = false)
    {
        return new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = fileType,
                GroupId = groupId
            },
            AllowDistribution = allowDistribution,
            AccessControlList = acl ?? AccessControlList.OwnerOnly
        };
    }

    public static UploadFileMetadata CreateWithContent(int fileType, string content, AccessControlList acl = null)
    {
        return new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = fileType,
                Content = content
            },

            AccessControlList = acl ?? AccessControlList.OwnerOnly
        };
    }
}