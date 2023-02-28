using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Hosting.Tests.AppAPI.Notifications;

public static class NotificationTestUtils
{
    public static (UploadInstructionSet, UploadFileMetadata) RandomEncryptedFileHeaderNoPayload(string jsonContents,
        TargetDrive targetDrive)
    {
        var keyHeader = KeyHeader.NewRandom16();
        
        var encryptedJsonContents = keyHeader.EncryptDataAesAsStream(jsonContents);
        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            AllowDistribution = true,
            PayloadIsEncrypted = true,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(encryptedJsonContents),
                FileType = 150
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var instructionSet = UploadInstructionSet.WithTargetDrive(targetDrive);

        return (instructionSet, fileMetadata);
    }
}