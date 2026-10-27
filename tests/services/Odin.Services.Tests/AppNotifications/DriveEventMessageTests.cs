using System.Threading.Tasks;
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
    // The real read rule: the owner and the zero-knowledge checks it makes need none of the service's dependencies
    private static readonly IDriveAclAuthorizationService ReadRule = new DriveAclAuthorizationService(null!, null!, null!);

    [TestCase(EncryptionType.ZkDrive, true, true, TestName = "AZkDriveFileReachesTheOwnerConsole")]
    [TestCase(EncryptionType.ZkDrive, false, false, TestName = "AZkDriveFileDoesNotReachAnApp")]
    [TestCase(EncryptionType.Aes, false, true, TestName = "AnyOtherFileStillReachesAnApp")]
    public async Task AZkDriveFileReachesOnlyASocketHoldingTheDriveKey(EncryptionType type, bool holdsMasterKey, bool expected)
    {
        var header = Header(type);
        var socket = Socket(holdsMasterKey);

        foreach (var (current, previous) in new (ServerFileHeader? Current, ServerFileHeader? Previous)[] { (header, null), (null, header) })
        {
            var message = new DriveEventMessage(ClientNotificationType.FileAdded, null, current, previous);
            Assert.That(await message.VisibleToAsync(socket, ReadRule), Is.EqualTo(expected));
        }
    }

    [Test]
    public async Task TheAclStillDecides()
    {
        var message = new DriveEventMessage(ClientNotificationType.FileAdded, null, Header(EncryptionType.Aes), null);
        Assert.That(await message.VisibleToAsync(Socket(holdsMasterKey: false, SecurityGroupType.Authenticated), ReadRule), Is.False);
    }

    [Test]
    public async Task ADeleteIsJudgedByTheFileAsItWas()
    {
        // The tombstone no longer says who could read the file; the ACL it had does
        var asItWas = Header(EncryptionType.Aes, AccessControlList.OwnerOnly);
        var tombstone = Header(EncryptionType.Aes, AccessControlList.Anonymous);

        var message = new DriveEventMessage(ClientNotificationType.FileDeleted, null, tombstone, asItWas);
        Assert.That(await message.VisibleToAsync(Socket(holdsMasterKey: false, SecurityGroupType.Authenticated), ReadRule), Is.False);
    }

    private static ServerFileHeader Header(EncryptionType type, AccessControlList? acl = null) => new()
    {
        EncryptedKeyHeader = new EncryptedKeyHeader
        {
            EncryptionVersion = 1,
            Type = type,
            Iv = ByteArrayUtil.GetRndByteArray(16),
            EncryptedAesKey = ByteArrayUtil.GetRndByteArray(48)
        },
        FileMetadata = new FileMetadata { IsEncrypted = true },
        ServerMetadata = new ServerMetadata { AccessControlList = acl ?? AccessControlList.OwnerOnly }
    };

    private static DeviceSocket Socket(bool holdsMasterKey, SecurityGroupType level = SecurityGroupType.Owner)
    {
        var masterKey = holdsMasterKey ? ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray() : null;
        return new DeviceSocket
        {
            DeviceOdinContext = new OdinContext
            {
                Caller = new CallerContext(new OdinId("frodo.dotyou.cloud"), masterKey, level)
            }
        };
    }
}
