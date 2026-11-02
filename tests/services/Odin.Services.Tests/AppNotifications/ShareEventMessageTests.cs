#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Tests.AppNotifications;

/// <summary>
/// A drive event on a Selective Share drive, for a member's socket (docs/selective-share-pr3-plan.md, section 9): the
/// file with their share's key while they may read it, the tombstone once a share naming them ends, nothing otherwise.
/// Any other socket is judged as before.
/// </summary>
public class ShareEventMessageTests
{
    private static readonly Guid DriveId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private static readonly Guid CircleId = Guid.NewGuid();
    private static readonly byte[] LockedFileKey = ByteArrayUtil.GetRndByteArray(45);

    [Test]
    public async Task AMemberGetsTheFileWithTheirSharesKey()
    {
        var (type, header) = Parse(await Message(shared: true, mayRead: true, named: true));

        Assert.That(type, Is.EqualTo(ClientNotificationType.FileModified));
        Assert.That((header.SharedSecretEncryptedKeyHeader.Type, header.SharedSecretEncryptedKeyHeader.EncryptedAesKey),
            Is.EqualTo((EncryptionType.Share, LockedFileKey)), "the share's key, never the drive-locked key header");
        Assert.That(header.Shares!.Single().CircleId, Is.EqualTo(CircleId), "their own share");
    }

    [Test]
    public async Task AMemberWhoseShareEndedGetsTheTombstone()
    {
        var (type, header) = Parse(await Message(shared: true, mayRead: false, named: true));

        Assert.That(type, Is.EqualTo(ClientNotificationType.FileDeleted));
        Assert.That(header.FileState, Is.EqualTo(FileState.Deleted));
        Assert.That(header.FileMetadata.AppData.Content, Is.Empty, "nothing of the file");
        Assert.That(header.SharedSecretEncryptedKeyHeader.EncryptedAesKey, Is.All.EqualTo(0), "no key");
    }

    [Test]
    public async Task ACallerNoShareNamesGetsNothing() =>
        Assert.That(await Message(shared: true, mayRead: false, named: false), Is.Null);

    [Test]
    public async Task AnyOtherSocketIsJudgedByTheReadRuleAlone()
    {
        // No share lookup applies (an owner caller, another drive): the read rule decides, the drive key included
        // (IDriveAclAuthorizationService.CallerMayReadAsync)
        Assert.That(await Message(shared: false, mayRead: false, named: false), Is.Null, "refused");
    }

    private static async Task<string?> Message(bool shared, bool mayRead, bool named)
    {
        var aclService = Substitute.For<IDriveAclAuthorizationService>();
        var rows = new List<DriveAclIndexRecord>();
        if (named)
        {
            // A withdrawn share (no bits) still names them; a live one is what reading needs, which the read rule decides
            rows.Add(new() { driveId = DriveId, fileId = FileId, aclMemberId = CircleId, permissions = mayRead ? 1 : 0, lockedFileKey = LockedFileKey });
        }

        CallerShares? shares = shared ? new CallerShares(new Dictionary<Guid, List<DriveAclIndexRecord>> { [FileId] = rows }, UnixTimeUtc.Now()) : null;
        aclService.FilesSharedWithCallerAsync(default, default!, default!).ReturnsForAnyArgs(Task.FromResult(shares));
        aclService.CallerMayReadAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(mayRead));

        var message = new DriveEventMessage(ClientNotificationType.FileModified, null, Header(), null);
        return await message.MessageForAsync(MemberSocket(), aclService);
    }

    private static (ClientNotificationType Type, SharedSecretEncryptedFileHeader Header) Parse(string? json)
    {
        Assert.That(json, Is.Not.Null, "the member's socket gets the event");
        var envelope = OdinSystemSerializer.Deserialize<Envelope>(json!)!;
        var notification = OdinSystemSerializer.Deserialize<ClientDriveNotification>(envelope.Data)!;
        return (envelope.NotificationType, notification.Header!);
    }

    private static ServerFileHeader Header() => new()
    {
        EncryptedKeyHeader = new EncryptedKeyHeader
        {
            EncryptionVersion = 1,
            Type = EncryptionType.ZkDrive,
            Iv = ByteArrayUtil.GetRndByteArray(16),
            EncryptedAesKey = ByteArrayUtil.GetRndByteArray(48)
        },
        FileMetadata = new FileMetadata(new InternalDriveFileId(DriveId, FileId))
        {
            IsEncrypted = true,
            FileState = FileState.Active,
            AppData = new AppFileMetaData { Content = "the file" }
        },
        ServerMetadata = new ServerMetadata
        {
            AccessControlList = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected }
        }
    };

    // Sam, connected, in the circle, with a grant on the drive, as a peer socket is
    private static DeviceSocket MemberSocket()
    {
        var context = new OdinContext
        {
            Caller = new CallerContext(new OdinId("sam.dotyou.cloud"), null, SecurityGroupType.Connected,
                circleIds: [new GuidId(CircleId)])
        };
        var grant = new DriveGrant
        {
            DriveId = DriveId,
            PermissionedDrive = new PermissionedDrive
            {
                Drive = new TargetDrive { Alias = DriveId, Type = Guid.NewGuid() },
                Permission = DrivePermission.Read | DrivePermission.Write
            }
        };
        context.SetPermissionContext(new PermissionContext(
            new Dictionary<string, PermissionGroup> { ["circle"] = new(new PermissionSet(), [grant], null, null) },
            ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray()));
        return new DeviceSocket { DeviceOdinContext = context };
    }

    private sealed class Envelope
    {
        public ClientNotificationType NotificationType { get; set; }
        public string Data { get; set; } = "";
    }
}
