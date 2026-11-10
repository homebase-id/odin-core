#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests.V2.ZeroKnowledge;
using Odin.Hosting.UnifiedV2.Circles;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;
using Odin.Services.Drives.Shares;
using Odin.Services.Peer.Encryption;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Refit;

namespace Odin.Hosting.Tests.V2.DevFixture;

/// <summary>
/// NOT A TEST: a local tool that sets up a collaborative Filesync folder's server side on the local dev server
/// (Odin.Hosting on :4444), for chat-kmp to run its client against.  Two unused dev identities: Pippin, who owns the
/// folder, and Tom, a member.  It reaches only those two (<see cref="DevServer"/>), runs only when asked, once, and
/// writes the keys and credentials it made to the file <c>ODIN_DEV_FIXTURE_OUT</c> names.
/// </summary>
/// <remarks>
/// Run: <c>ODIN_DEV_FIXTURE_OUT=/path/out.json dotnet test tests/apps/Odin.Hosting.Tests.V2 -c Release
/// --filter FullyQualifiedName~CollaborativeFolderDevFixture</c>.  It refuses to run when either identity already has a
/// password, so it never changes an identity someone uses.
/// </remarks>
[TestFixture]
[Explicit("A local tool for the dev server, not a test")]
public class CollaborativeFolderDevFixture
{
    private static readonly string Pippin = Identities.Pippin;
    private static readonly string Tom = Identities.TomBombadil;
    private const int DevServerPort = 4444;
    private static readonly DevServer Dev = new(DevServerPort, new HashSet<string> { Pippin, Tom });

    // What each circle grants on the Filesync drive
    private const DrivePermission FolderPermissions =
        DrivePermission.Read | DrivePermission.Write | DrivePermission.React | DrivePermission.Comment;

    // chat-kmp's Filesync registration (AppConfig.kt filesyncAppRegistration)
    private static readonly Guid FilesyncAppId = Guid.Parse("03ae1a49cdae483f9da7fedc0e8f20b9");
    private static readonly TargetDrive FilesyncDrive = new()
    {
        Alias = Guid.Parse("cfceb937-b868-496b-91cb-3402f18320f8"),
        Type = Guid.Parse("5da48122-e946-487a-8b9d-589642f3c91d")
    };

    // A chat-like app on Tom's identity, which chat-kmp's harness sends as
    private static readonly Guid ChatLikeAppId = Guid.Parse("7c1f2e0a-5d3b-4f6e-9a8c-1b2d3e4f5a6b");

    [Test]
    public async Task SetUpACollaborativeFilesyncFolderOnTheDevServer()
    {
        var outPath = Environment.GetEnvironmentVariable("ODIN_DEV_FIXTURE_OUT");
        Assert.That(outPath, Is.Not.Null.And.Not.Empty, "Set ODIN_DEV_FIXTURE_OUT to the file the keys and credentials go to");
        Assert.That(File.Exists(outPath), Is.False, $"{outPath} exists: the fixture is set up already");

        foreach (var identity in new[] { Pippin, Tom })
        {
            Assert.That(await IsPasswordSetAsync(identity), Is.False,
                $"{identity} has a password: someone uses it, so this tool won't touch it");
        }

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var result = new Dictionary<string, object> { ["devServer"] = $"https://{{identity}}:{DevServerPort}", ["password"] = password };

        // 1. Owners, initialized and zero knowledge, each with Filesync and its Private Share drive
        var pippin = await OwnerSession.LoginToDevServerAsync(Dev, Pippin, password, setPassword: true);
        var tom = await OwnerSession.LoginToDevServerAsync(Dev, Tom, password, setPassword: true);
        var pippinFilesync = await SetUpOwnerAsync(pippin, password);
        var tomFilesync = await SetUpOwnerAsync(tom, password);

        // 2. Tom's chat-like app: sends to peers and reads his Filesync drive
        var chat = await AppSession.SetupAsync(tom, new PermissionSetGrantRequest
        {
            Drives = [new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = FilesyncDrive, Permission = DrivePermission.ReadWrite } }],
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitRead, PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections)
        }, knownAppId: ChatLikeAppId);

        // 3. Pippin and Tom connected, Tom reviewed
        await ConnectAsync(pippin, tom);

        // 4. Two circles of Pippin's Filesync with every share bit on its drive, each with a real key: locked with an app key
        // for the owner's devices, and sealed to Tom's Filesync drive for him.  Tom in both.
        var appZkK = RandomNumberGenerator.GetBytes(16);
        var circles = new List<object>();
        foreach (var name in new[] { "filesync-edit", "Tom (individual)" })
        {
            circles.Add(await CircleWithTomAsync(pippin, pippinFilesync.App, name, appZkK, tomFilesync.Keys.WriteOnlyPublicKeyJwk));
        }

        var pippinResult = OwnerResult(pippin, pippinFilesync, circles);
        pippinResult["filesyncAppZkKey"] = Convert.ToBase64String(appZkK);
        var tomResult = OwnerResult(tom, tomFilesync, []);
        tomResult["chatApp"] = AppResult(chat);
        result["pippin"] = pippinResult;
        result["tom"] = tomResult;

        await WriteFixtureAsync(outPath!, result);
        TestContext.Out.WriteLine($"Set up; keys and credentials in {outPath}");
    }

    /// <summary>
    /// A follow-up to <see cref="SetUpACollaborativeFilesyncFolderOnTheDevServer"/>: a Filesync device on each identity that
    /// holds the app's zero-knowledge key, signed in as the app does (the consent page's part played as the browser does),
    /// since a client-locked write needs it; and, on Pippin, two new circles whose owner copy is locked with that key
    /// (a circle's key is set once, and the first two were locked with a stand-in), Tom in both with his sealed copy.
    /// Rewrites the fixture's file; refuses to run twice.
    /// </summary>
    [Test]
    public async Task GiveFilesyncDevicesTheirKeyAndCirclesLockedWithIt()
    {
        var (outPath, root) = await ReadFixtureAsync();
        Assert.That(root.GetProperty("pippin").TryGetProperty("filesyncDevice", out _), Is.False, "done already");

        var password = root.GetProperty("password").GetString()!;
        var pippin = await OwnerSession.LoginToDevServerAsync(Dev, Pippin, password, setPassword: false);
        var tom = await OwnerSession.LoginToDevServerAsync(Dev, Tom, password, setPassword: false);

        var (pippinDevice, pippinAppKey) = await ZkAppSteps.SignInAsync(pippin,
            FilesyncAppId, Convert.FromBase64String(root.GetProperty("pippin").GetProperty("zkMasterKey").GetString()!));
        var (tomDevice, tomAppKey) = await ZkAppSteps.SignInAsync(tom,
            FilesyncAppId, Convert.FromBase64String(root.GetProperty("tom").GetProperty("zkMasterKey").GetString()!));

        var tomsPublicKey = root.GetProperty("tom").GetProperty("filesyncDrive").GetProperty("writeOnlyPublicKeyJwk").GetString()!;
        var circles = new List<object>();
        foreach (var name in new[] { "filesync-edit (keyed)", "Tom (individual, keyed)" })
        {
            circles.Add(await CircleWithTomAsync(pippin, pippinDevice, name, pippinAppKey, tomsPublicKey));
        }

        var pippinResult = JsonSerializer.Deserialize<Dictionary<string, object>>(root.GetProperty("pippin").GetRawText())!;
        pippinResult["standInCircles"] = pippinResult["circles"];
        pippinResult["circles"] = circles;
        pippinResult["filesyncAppZkKey"] = Convert.ToBase64String(pippinAppKey);
        pippinResult["filesyncDevice"] = AppResult(pippinDevice);
        var tomResult = JsonSerializer.Deserialize<Dictionary<string, object>>(root.GetProperty("tom").GetRawText())!;
        tomResult["filesyncAppZkKey"] = Convert.ToBase64String(tomAppKey);
        tomResult["filesyncDevice"] = AppResult(tomDevice);

        var result = new Dictionary<string, object>
        {
            ["devServer"] = root.GetProperty("devServer"), ["password"] = password, ["pippin"] = pippinResult, ["tom"] = tomResult
        };
        await WriteFixtureAsync(outPath, result);
        TestContext.Out.WriteLine($"Devices and keyed circles in {outPath}");
    }

    /// <summary>
    /// Checks Pippin's Filesync device can do what chat-kmp's folder upload does: a new file with its key header locked
    /// with the drive key, shared with both keyed circles in the same upload (format 3, by the global transit id it picks).
    /// Hard-deletes the file after, so nothing is left.
    /// </summary>
    [Test]
    public async Task PippinsFilesyncDeviceWritesAClientLockedSharedFile()
    {
        var (_, root) = await ReadFixtureAsync();
        var pippin = root.GetProperty("pippin");
        var owner = await OwnerSession.LoginToDevServerAsync(Dev, Pippin, root.GetProperty("password").GetString()!, setPassword: false);
        var device = AppFromJson(owner, pippin.GetProperty("filesyncDevice"));

        var fileKey = KeyHeader.NewRandom16();
        var gtid = Guid.NewGuid();
        var shares = new List<ShareRequest>();
        foreach (var circle in pippin.GetProperty("circles").EnumerateArray())
        {
            var circleId = circle.GetProperty("circleId").GetGuid();
            shares.Add(new ShareRequest
            {
                FileId = Guid.Empty,
                CircleId = circleId,
                Permissions = FolderPermissions,
                LockedFileKey = ShareKeys.LockFileKeyByGlobalTransitId(Convert.FromBase64String(circle.GetProperty("circleKey").GetString()!),
                    fileKey.AesKey.GetKey(), FilesyncDrive.Alias, gtid, circleId)
            });
        }

        var (upload, _, _, _) = await device.Drives.Writer.CreateEncryptedFile(FilesyncDrive.Alias,
            ZkTestFiles.Metadata(Guid.NewGuid(), "a dev fixture probe"), transitOptions: null!, new UploadManifest { PayloadDescriptors = [] },
            [], keyHeader: fileKey, lockedKeyHeader: ZkBrowser.LockFileKey(fileKey,
                Convert.FromBase64String(pippin.GetProperty("filesyncDrive").GetProperty("driveKey").GetString()!)),
            shares: shares, globalTransitId: gtid);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        var delete = await device.Drives.Writer.HardDeleteBatch(FilesyncDrive.Alias, new HardDeleteBatchRequestV2
        {
            Files = [new FileVersionTagV2 { FileId = upload.Content!.FileId, VersionTag = upload.Content.NewVersionTag }]
        });
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
    }

    /// <summary>
    /// Checks the setup as chat-kmp will use it, with Tom's chat-like app: Pippin's Filesync public key by slug, Tom's
    /// sealed copy of each circle key opened with his Filesync drive's key, and the send status route.  Changes nothing.
    /// </summary>
    [Test]
    public async Task TomsChatAppReachesWhatTheFixtureSetUp()
    {
        var (_, root) = await ReadFixtureAsync();
        var tomOwner = await OwnerSession.LoginToDevServerAsync(Dev, Tom, root.GetProperty("password").GetString()!, setPassword: false);
        var tom = root.GetProperty("tom");
        var chat = AppFromJson(tomOwner, tom.GetProperty("chatApp"));

        var pippin = root.GetProperty("pippin");
        var publicKey = await chat.RefitFor<IPeerAppDriveHttpClientApiV2>().GetDriveWriteOnlyPublicKey(Pippin, "filesync", "filesync");
        Assert.That(publicKey.StatusCode, Is.EqualTo(HttpStatusCode.OK), publicKey.Error?.Content);
        Assert.That(publicKey.Content!.PublicKeyJwk, Is.EqualTo(pippin.GetProperty("filesyncDrive").GetProperty("writeOnlyPublicKeyJwk").GetString()),
            "Pippin's Filesync drive key, by slug");

        var tomsPrivateKey = Convert.FromBase64String(tom.GetProperty("filesyncDrive").GetProperty("writeOnlyPrivateKeyPkcs8").GetString()!);
        foreach (var circle in pippin.GetProperty("circles").EnumerateArray())
        {
            var circleId = circle.GetProperty("circleId").GetGuid();
            var copy = await chat.RefitFor<ICircleShareKeyHttpClient>().GetMySealedShareKey(Pippin, circleId);
            Assert.That(copy.StatusCode, Is.EqualTo(HttpStatusCode.OK), copy.Error?.Content);
            Assert.That(Convert.ToBase64String(ShareKeys.Unseal(copy.Content!.SealedKey, tomsPrivateKey, ShareKeys.SealedAad(circleId, Tom))),
                Is.EqualTo(circle.GetProperty("circleKey").GetString()), $"Tom opens his copy of {circle.GetProperty("name").GetString()}'s key");
        }

        var status = await chat.Drives.Writer.GetPeerSendStatus((OdinId)Pippin, FilesyncDrive.Alias, Guid.NewGuid(), null);
        Assert.That((status.StatusCode, status.Content?.Pending), Is.EqualTo((HttpStatusCode.OK, (bool?)false)), status.Error?.Content);
    }

    private sealed record OwnersFilesync(byte[] ZkMasterKey, ZkBrowser.DriveKeys Keys, AppSession App);

    private static async Task<OwnersFilesync> SetUpOwnerAsync(OwnerSession owner, string password)
    {
        await owner.Admin.InitializeIdentity();
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(owner, password);

        await owner.Admin.RegisterApp(FilesyncAppId, new PermissionSetGrantRequest(), appSlug: new OdinSlug("filesync"));
        var (_, keys) = await ZkOwnerSteps.CreateZeroKnowledgeDriveAsync(owner, zkMasterKey, DriveSharingModel.SelectiveShare,
            FilesyncAppId, "Filesync", FilesyncDrive, driveSlug: new OdinSlug("filesync"), driveTypeSlug: new OdinSlug("filesync"));
        var app = await AppSession.SetupAsync(owner, FilesyncDrive, FolderPermissions, [PermissionKeys.ReadConnections],
            knownAppId: FilesyncAppId);
        return new OwnersFilesync(zkMasterKey, keys, app);
    }

    private static async Task ConnectAsync(OwnerSession owner, OwnerSession member)
    {
        await PeerFlow.ConnectAsync(owner, member);
        var review = await new V2ConnectionNetworkClient(owner.Identity, owner.Factory).MarkReviewedAsync(member.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review: {review.StatusCode} {review.Error?.Content}");
    }

    private static async Task<object> CircleWithTomAsync(OwnerSession pippin, AppSession filesync, string name, byte[] appZkK,
        string tomsWriteOnlyPublicKeyJwk)
    {
        var circleId = Guid.NewGuid();
        await pippin.Admin.CreateCircle(circleId, name, new PermissionSetGrantRequest
        {
            Drives = [new DriveGrantRequest
            {
                PermissionedDrive = new PermissionedDrive { Drive = FilesyncDrive, Permission = FolderPermissions }
            }]
        }, FilesyncAppId);

        var circleKey = RandomNumberGenerator.GetBytes(16);
        var keys = filesync.RefitFor<ICircleShareKeyHttpClient>();
        var appKeyLocked = ShareKeys.Lock(appZkK, circleKey, circleId.ToByteArray(), RandomNumberGenerator.GetBytes(12));
        var set = await keys.SetShareKey(circleId, new CircleShareKeyData { AppKeyLockedShareKey = appKeyLocked });
        Assert.That(set.IsSuccessStatusCode, Is.True, $"circle key: {set.StatusCode} {set.Error?.Content}");

        await AppCircles.AddMemberAsync(filesync, circleId, Tom);

        var sealedForTom = ShareKeys.Seal(circleKey, tomsWriteOnlyPublicKeyJwk, ShareKeys.SealedAad(circleId, Tom));
        var copy = await keys.SetSealedShareKeys(circleId, new SetSealedShareKeysRequest
        {
            SealedFrom = appKeyLocked,
            Members = [new SealedShareKey { OdinId = Tom, SealedKey = sealedForTom }]
        });
        Assert.That(copy.IsSuccessStatusCode, Is.True, $"Tom's copy: {copy.StatusCode} {copy.Error?.Content}");

        return new Dictionary<string, object>
        {
            ["circleId"] = circleId,
            ["name"] = name,
            ["permissions"] = "Read|Write|React|Comment",
            ["circleKey"] = Convert.ToBase64String(circleKey),
            ["appKeyLockedShareKey"] = Convert.ToBase64String(appKeyLocked),
            ["sealedForTom"] = Convert.ToBase64String(sealedForTom)
        };
    }

    private static Dictionary<string, object> OwnerResult(OwnerSession owner, OwnersFilesync filesync, List<object> circles) => new()
        {
            ["identity"] = owner.Identity.DomainName,
            ["zkMasterKey"] = Convert.ToBase64String(filesync.ZkMasterKey),
            ["filesyncDrive"] = new Dictionary<string, object>
            {
                ["alias"] = FilesyncDrive.Alias.Value,
                ["type"] = FilesyncDrive.Type.Value,
                ["driveKey"] = Convert.ToBase64String(filesync.Keys.DriveKey),
                ["writeOnlyPublicKeyJwk"] = filesync.Keys.WriteOnlyPublicKeyJwk,
                ["writeOnlyPrivateKeyPkcs8"] = Convert.ToBase64String(filesync.Keys.WriteOnlyPrivateKey)
            },
            ["filesyncApp"] = AppResult(filesync.App),
            ["circles"] = circles
        };

    private static Dictionary<string, object> AppResult(AppSession app) => new()
    {
        ["appId"] = app.AppId,
        ["clientAuthToken"] = app.Token.ToPortableBytes64(),
        ["sharedSecret"] = Convert.ToBase64String(app.SharedSecret)
    };

    // The inverse of AppResult
    private static AppSession AppFromJson(OwnerSession owner, JsonElement app) =>
        AppSession.FromToken(owner, app.GetProperty("appId").GetGuid(),
            ClientAuthenticationToken.FromPortableBytes64(app.GetProperty("clientAuthToken").GetString()!),
            Convert.FromBase64String(app.GetProperty("sharedSecret").GetString()!));

    // The fixture's file, which the first test wrote
    private static async Task<(string Path, JsonElement Root)> ReadFixtureAsync()
    {
        var path = Environment.GetEnvironmentVariable("ODIN_DEV_FIXTURE_OUT");
        Assert.That(File.Exists(path), Is.True, "Set ODIN_DEV_FIXTURE_OUT to the fixture's file");
        return (path!, JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(path!)));
    }

    private static Task WriteFixtureAsync(string path, Dictionary<string, object> result) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

    private static async Task<bool> IsPasswordSetAsync(string identity)
    {
        using var client = new HttpClient(Dev.CreateHandler()) { BaseAddress = Dev.BaseFor(identity) };
        var response = await RestService.For<IOwnerAuthenticationClient>(client).IsPasswordSet();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{identity}: is a password set?");
        return response.Content;
    }
}
