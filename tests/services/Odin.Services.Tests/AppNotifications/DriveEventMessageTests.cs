using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Tests.AppNotifications;

/// <summary>
/// A zero-knowledge drive's own file reaches only a socket holding the drive's key, as a query leaves it
/// out for anyone else (docs/zero-knowledge-drive-api-plan.md, decision 5).  The check runs before any
/// result shape is built, so a minimal socket gets no reference to it either, and building the message
/// never throws for a socket it isn't for.
/// </summary>
public class DriveEventMessageTests
{
    [TestCase(EncryptionType.ZkDrive, true, true, TestName = "AZkDriveFileReachesTheOwnerConsole")]
    [TestCase(EncryptionType.ZkDrive, false, false, TestName = "AZkDriveFileDoesNotReachAnApp")]
    [TestCase(EncryptionType.Aes, false, true, TestName = "AnyOtherFileStillReachesAnApp")]
    public async Task AZkDriveFileReachesOnlyASocketHoldingTheDriveKey(EncryptionType type, bool holdsMasterKey, bool expected)
    {
        var header = new ServerFileHeader
        {
            EncryptedKeyHeader = new EncryptedKeyHeader
            {
                EncryptionVersion = 1,
                Type = type,
                Iv = ByteArrayUtil.GetRndByteArray(16),
                EncryptedAesKey = ByteArrayUtil.GetRndByteArray(48)
            },
            FileMetadata = new FileMetadata { IsEncrypted = true },
            ServerMetadata = new ServerMetadata { AccessControlList = AccessControlList.OwnerOnly }
        };

        var aclService = Substitute.For<IDriveAclAuthorizationService>();
        aclService.CallerHasPermission(Arg.Any<AccessControlList>(), Arg.Any<IOdinContext>()).Returns(true);

        var masterKey = holdsMasterKey ? ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray() : null;
        var socket = new DeviceSocket
        {
            DeviceOdinContext = new OdinContext
            {
                Caller = new CallerContext(new OdinId("frodo.dotyou.cloud"), masterKey, SecurityGroupType.Owner)
            }
        };

        foreach (var (current, previous) in new[] { (header, (ServerFileHeader)null), (null, header) })
        {
            var message = new DriveEventMessage(ClientNotificationType.FileAdded, null, current, previous);
            Assert.That(await message.VisibleToAsync(socket, aclService), Is.EqualTo(expected));
        }
    }
}
