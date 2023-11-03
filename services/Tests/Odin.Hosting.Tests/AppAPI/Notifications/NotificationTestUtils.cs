using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.AppAPI.Notifications;

public static class NotificationTestUtils
{
    public static (UploadInstructionSet, UploadFileMetadata) RandomEncryptedFileHeaderNoPayload(string jsonContents,
        TargetDrive targetDrive)
    {
        var keyHeader = KeyHeader.NewRandom16();
        
        var encryptedJsonContents = keyHeader.EncryptDataAesAsStream(jsonContents);
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            PayloadIsEncrypted = true,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(encryptedJsonContents),
                FileType = 150
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var instructionSet = UploadInstructionSet.WithTargetDrive(targetDrive);

        return (instructionSet, fileMetadata);
    }
}