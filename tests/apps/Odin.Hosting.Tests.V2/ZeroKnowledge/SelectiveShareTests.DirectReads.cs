#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.UnifiedV2.Drive.Shares;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Peer.Encryption;
using Refit;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A member's device reads what is shared with it straight from the sharer's server, with the peer-notification token its
/// own server fetched for the peer socket: Read on the zero-knowledge drives its connection reaches, and nothing more.
/// </summary>
public partial class SelectiveShareTests
{
    [Test]
    public async Task AMembersPeerTokenReadsASharedFileStraightFromTheSharersServer()
    {
        var s = await ArrangeAsync();
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var (fileId, locked, _) = await s.UploadClientLockedAsync(payload);
        var (notShared, _, _) = await s.UploadClientLockedAsync(SamplePayloadDefinitions.GetPayloadDefinition2());
        var lockedFileKey = LockedFileKey();
        var write = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, DrivePermission.Read, lockedFileKey: lockedFileKey)]
        });
        Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.OK), write.Error?.Content);

        var direct = await PeerTokenCaller.ForAsync(s.Sam, s.Frodo);

        var header = await direct.Drives.Reader.GetFileHeaderAsync(s.Drive.Alias, fileId);
        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.OK), header.Error?.Content);
        var key = header.Content!.SharedSecretEncryptedKeyHeader;
        Assert.That((key.Type, key.EncryptedAesKey, key.Iv), Is.EqualTo((EncryptionType.Share, lockedFileKey, locked.Iv)),
            "the share's locked file key, as on the relay");

        var bytes = await PayloadBytesAsync(direct.Drives.Reader.GetPayloadAsync(s.Drive.Alias, fileId, payload.Key), "direct");
        var relayed = await PayloadBytesAsync(s.Sam.Drives.Peer.GetPayloadAsync(s.Frodo.Identity, s.Drive.Alias, fileId, payload.Key),
            "through Sam's own server");
        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(relayed), "the same encrypted bytes the relay brings");

        var other = await direct.Drives.Reader.GetFileHeaderAsync(s.Drive.Alias, notShared);
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "a file not shared with Sam");

        var delete = await direct.Drives.Writer.SoftDeleteFile(s.Drive.Alias, fileId);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "the token only reads");
        Assert.That((await s.HeaderAsync(fileId)).FileState, Is.EqualTo(FileState.Active), "the file after the refused delete");

        Assert.That((await s.Shares.WithdrawShare(s.Drive.Alias, fileId, s.CircleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var withdrawn = await direct.Drives.Reader.GetPayloadAsync(s.Drive.Alias, fileId, payload.Key);
        Assert.That(withdrawn.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "the payload once the share is withdrawn");
    }

    [Test]
    public async Task AMembersPeerTokenReadsNothingOnADriveTheServerOpens()
    {
        var s = await ArrangeAsync();
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var upload = await s.Frodo.V1.Drive.UploadNewFile(MeshDrive,
            SampleMetadataData.CreateWithContent(fileType: 100, "on the mesh", new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Connected }),
            new UploadManifest { PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList() }, [payload]);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var fileId = upload.Content!.File.FileId;
        await PayloadBytesAsync(s.Sam.Drives.Peer.GetPayloadAsync(s.Frodo.Identity, MeshDrive.Alias, fileId, payload.Key),
            "precondition: Sam's connection reads it through his own server");

        var direct = await PeerTokenCaller.ForAsync(s.Sam, s.Frodo);

        var header = await direct.Drives.Reader.GetFileHeaderAsync(MeshDrive.Alias, fileId);
        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "the header of a file the server would open for him");
        var read = await direct.Drives.Reader.GetPayloadAsync(MeshDrive.Alias, fileId, payload.Key);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "its payload");
    }

    private static async Task<byte[]> PayloadBytesAsync(Task<ApiResponse<HttpContent>> read, string how)
    {
        var response = await read;
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{how}: {response.Error?.Content}");
        return await response.Content!.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Sam's device on Frodo's server with the token Sam's own server fetched for the peer socket, as a daemon would
    /// download what Frodo shares with Sam.
    /// </summary>
    private sealed class PeerTokenCaller : IV2Caller
    {
        public OdinId Identity { get; }
        public InProcessApiClientFactory Factory { get; }
        public AuthV2Client Auth { get; }
        public DriveHandles Drives { get; }
        public V1Handles V1 { get; }

        private PeerTokenCaller(OdinHost host, OdinId sharer, ClientAuthenticationToken token, SensitiveByteArray sharedSecret)
        {
            Identity = sharer;
            Factory = new InProcessApiClientFactory(host, YouAuthDefaults.XTokenCookieName, token, sharedSecret);
            Auth = new AuthV2Client(Identity, Factory);
            Drives = new DriveHandles(Identity, Factory);
            V1 = new V1Handles(Identity, Factory);
        }

        public static async Task<PeerTokenCaller> ForAsync(OwnerSession member, OwnerSession sharer)
        {
            var response = await member.RefitFor<IDrivePeerNotificationHttpClientApiV2>()
                .GetToken(new GetRemoteTokenRequest { Identity = sharer.Identity });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
            var token = ClientAuthenticationToken.FromPortableBytes64(response.Content!.AuthenticationToken64);
            Assert.That(token.ClientTokenType, Is.EqualTo(ClientTokenType.PeerNotificationSubscriber));
            return new PeerTokenCaller(sharer.Host, sharer.Identity, token, response.Content.SharedSecret.ToSensitiveByteArray());
        }
    }
}
