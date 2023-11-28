using System;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests;

public static class SampleMetadataDataDefinitions
{
    public static UploadFileMetadata Create(int fileType, Guid? groupId = null, AccessControlList acl = null)
    {
        return new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = fileType,
                GroupId = groupId ?? default
            },

            AccessControlList = acl ?? AccessControlList.OwnerOnly
        };
    }
}