using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.AppAPI.Notifications;

public static class NotificationTestUtils
{
    public static (UploadInstructionSet, UploadFileMetadata) RandomEncryptedFileHeaderNoPayload(string jsonContents,
        TargetDrive targetDrive)
    {
        var keyHeader = KeyHeader.NewRandom16();

        var encryptedJsonContent64 = keyHeader.EncryptDataAes(jsonContents.ToUtf8ByteArray()).ToBase64();
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Content = encryptedJsonContent64,
                FileType = 150
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var instructionSet = UploadInstructionSet.WithTargetDrive(targetDrive);

        return (instructionSet, fileMetadata);
    }
}