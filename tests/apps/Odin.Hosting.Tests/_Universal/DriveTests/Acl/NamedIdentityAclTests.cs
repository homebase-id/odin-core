using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Util;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests.Acl;

/// <summary>
/// A file's ACL can name identities (<see cref="AccessControlList.OdinIdList"/>) as well as circles: a reader is admitted
/// when they meet the security group and are in one of the named circles or are one of the named identities (#1914).
/// Every read path is covered -- direct get, query (and its post-query ACL filter), and feed distribution.
/// </summary>
public class NamedIdentityAclTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities:
            [TestIdentities.Frodo, TestIdentities.Samwise, TestIdentities.Merry, TestIdentities.Pippin]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        // The post-query ACL filter logs an error for every file the index returned but the ACL refused;
        // a clean log means the index and the ACL decision agree on named identities.
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task ConnectedIdentityNamedInAcl_CanGetFile_AndUnnamedConnectionCannot()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var pippin = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

        var drive = TargetDrive.NewTargetDrive();
        var (_, clubCircleId) = await ConnectReaders(frodo, drive, sam, merry, pippin);

        try
        {
            var namesSam = await Upload(frodo, drive, new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                OdinIdList = [sam.OdinId]
            });

            var namesClubOrSam = await Upload(frodo, drive, new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                CircleIdList = [clubCircleId],
                OdinIdList = [sam.OdinId]
            });

            // Named identity only: Sam, and no one else
            Assert.That(await CanGetOverPeer(sam, frodo, namesSam), Is.True);
            Assert.That(await CanGetOverPeer(merry, frodo, namesSam), Is.False);
            Assert.That(await CanGetOverPeer(pippin, frodo, namesSam), Is.False);

            // Circle or identity: Sam by name, Pippin by circle, not Merry
            Assert.That(await CanGetOverPeer(sam, frodo, namesClubOrSam), Is.True);
            Assert.That(await CanGetOverPeer(pippin, frodo, namesClubOrSam), Is.True);
            Assert.That(await CanGetOverPeer(merry, frodo, namesClubOrSam), Is.False);
        }
        finally
        {
            await DisconnectReaders(frodo, sam, merry, pippin);
        }
    }

    [Test]
    public async Task ConnectedIdentityNamedInAcl_FindsFileInQuery_AndUnnamedConnectionDoesNot()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var pippin = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

        var drive = TargetDrive.NewTargetDrive();
        var (_, clubCircleId) = await ConnectReaders(frodo, drive, sam, merry, pippin);

        try
        {
            var namesSam = await Upload(frodo, drive, new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                OdinIdList = [sam.OdinId]
            });

            var namesClubOrSam = await Upload(frodo, drive, new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                CircleIdList = [clubCircleId],
                OdinIdList = [sam.OdinId]
            });

            // Readable by every connection, so each query below is known to reach the drive
            var anyConnection = await Upload(frodo, drive, new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected
            });

            var samFinds = await QueryOverPeer(sam, frodo, drive);
            Assert.That(samFinds, Is.EquivalentTo(new[] { namesSam.FileId, namesClubOrSam.FileId, anyConnection.FileId }));

            var pippinFinds = await QueryOverPeer(pippin, frodo, drive);
            Assert.That(pippinFinds, Is.EquivalentTo(new[] { namesClubOrSam.FileId, anyConnection.FileId }));

            var merryFinds = await QueryOverPeer(merry, frodo, drive);
            Assert.That(merryFinds, Is.EquivalentTo(new[] { anyConnection.FileId }));
        }
        finally
        {
            await DisconnectReaders(frodo, sam, merry, pippin);
        }
    }

    [Test]
    public async Task NamedIdentity_MustStillMeetTheSecurityGroup()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await frodo.DriveManager.CreateDrive(drive, "Named identity ACL drive", "", allowAnonymousReads: false);

        // A logged-in identity which is not connected: a YouAuth domain, admitted at Authenticated
        var domain = new AsciiDomainName($"{Guid.NewGuid():n}.example.org");
        var guest = await RegisterYouAuthDomain(frodo, drive, domain);
        var guestDrive = new UniversalDriveApiClient(frodo.OdinId, guest);

        var connectedNamingGuest = await Upload(frodo, drive, new AccessControlList
        {
            RequiredSecurityGroup = SecurityGroupType.Connected,
            OdinIdList = [domain.DomainName]
        });

        var authenticatedNamingGuest = await Upload(frodo, drive, new AccessControlList
        {
            RequiredSecurityGroup = SecurityGroupType.Authenticated,
            OdinIdList = [domain.DomainName]
        });

        var authenticatedNamingSomeoneElse = await Upload(frodo, drive, new AccessControlList
        {
            RequiredSecurityGroup = SecurityGroupType.Authenticated,
            OdinIdList = [TestIdentities.Samwise.OdinId]
        });

        try
        {
            // Direct get
            Assert.That((await guestDrive.GetFileHeader(connectedNamingGuest)).IsSuccessStatusCode, Is.False,
                "being named does not lift an Authenticated caller to Connected");
            Assert.That((await guestDrive.GetFileHeader(authenticatedNamingGuest)).IsSuccessStatusCode, Is.True);
            Assert.That((await guestDrive.GetFileHeader(authenticatedNamingSomeoneElse)).IsSuccessStatusCode, Is.False);

            // Query
            var response = await guestDrive.QueryBatch(QueryDrive(drive));
            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Content.SearchResults.Select(r => r.FileId), Is.EquivalentTo(new[] { authenticatedNamingGuest.FileId }));
        }
        finally
        {
            await frodo.YouAuth.DeleteDomainRegistration(domain);
        }
    }

    [Test]
    public async Task UploadRejectsAnInvalidIdentity_AndStoresNamedIdentitiesCanonically()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await frodo.DriveManager.CreateDrive(drive, "Named identity ACL drive", "", allowAnonymousReads: false);

        var invalid = await frodo.DriveRedux.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100, acl: new AccessControlList
        {
            RequiredSecurityGroup = SecurityGroupType.Connected,
            OdinIdList = [TestIdentities.Samwise.OdinId, "not a domain!"]
        }));

        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var sam = TestIdentities.Samwise.OdinId.DomainName;
        var merry = TestIdentities.Merry.OdinId.DomainName;
        var file = await Upload(frodo, drive, new AccessControlList
        {
            RequiredSecurityGroup = SecurityGroupType.Connected,
            OdinIdList = [sam.ToUpperInvariant(), sam, merry, merry.ToUpperInvariant()]
        });

        var header = await frodo.DriveRedux.GetFileHeader(file);
        Assert.That(header.IsSuccessStatusCode, Is.True);
        Assert.That(header.Content.ServerMetadata.AccessControlList.OdinIdList, Is.EqualTo(new List<string> { sam, merry }));
    }

    [Test]
    public async Task ChannelPostNamingAFollower_IsDistributedToThatFollowerOnly()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var pippin = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

        var channel = new TargetDrive
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await ConnectReaders(frodo, channel, sam, merry, pippin, allowSubscriptions: true);
        await sam.Follower.FollowIdentity(frodo.OdinId, FollowerNotificationType.AllNotifications);
        await merry.Follower.FollowIdentity(frodo.OdinId, FollowerNotificationType.AllNotifications);

        try
        {
            var metadata = SampleMetadataData.Create(fileType: 100, acl: new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                OdinIdList = [sam.OdinId]
            }, allowDistribution: true);
            metadata.AppData.Content = "For Sam's eyes only";

            // Encrypted, so the router picks the recipients by ACL before anything is queued.  (An unencrypted
            // post is queued for every follower and refused in the outbox worker, where a refusal is retried
            // as recoverable, so the outbox never drains for a test to wait on.)
            var (upload, _) = await frodo.DriveRedux.UploadNewEncryptedMetadata(channel, metadata);
            Assert.That(upload.IsSuccessStatusCode, Is.True);
            var post = upload.Content.GlobalTransitIdFileIdentifier;

            await frodo.DriveRedux.WaitForFeedOutboxDistribution(channel);

            Assert.That(await FeedHasPost(sam, post), Is.True, "Sam is named in the ACL");
            Assert.That(await FeedHasPost(merry, post), Is.False, "Merry follows Frodo but is not named in the ACL");
        }
        finally
        {
            await sam.Follower.UnfollowIdentity(frodo.OdinId);
            await merry.Follower.UnfollowIdentity(frodo.OdinId);
            await DisconnectReaders(frodo, sam, merry, pippin);
        }
    }

    /// <summary>
    /// Creates <paramref name="drive"/> on the owner and connects the readers, each with read access to it:
    /// Sam and Merry through a readers circle, Pippin through a club circle.  Each connection is reviewed,
    /// so it reads at Connected.
    /// </summary>
    private static async Task<(Guid readersCircleId, Guid clubCircleId)> ConnectReaders(OwnerApiClientRedux owner, TargetDrive drive,
        OwnerApiClientRedux sam, OwnerApiClientRedux merry, OwnerApiClientRedux pippin, bool allowSubscriptions = false)
    {
        var createDrive = await owner.DriveManager.CreateDrive(drive, "Named identity ACL drive", "", allowAnonymousReads: false,
            allowSubscriptions: allowSubscriptions);
        Assert.That(createDrive.IsSuccessStatusCode, Is.True);

        var readersCircleId = Guid.NewGuid();
        await owner.Network.CreateCircle(readersCircleId, "Readers", ReadGrant(drive));

        var clubCircleId = Guid.NewGuid();
        await owner.Network.CreateCircle(clubCircleId, "Club", ReadGrant(drive));

        await Connect(owner, sam, readersCircleId);
        await Connect(owner, merry, readersCircleId);
        await Connect(owner, pippin, clubCircleId);

        return (readersCircleId, clubCircleId);
    }

    private static async Task Connect(OwnerApiClientRedux owner, OwnerApiClientRedux reader, Guid circleId)
    {
        var send = await reader.Connections.SendConnectionRequest(owner.OdinId);
        Assert.That(send.IsSuccessStatusCode, Is.True);

        var accept = await owner.Connections.AcceptConnectionRequest(reader.OdinId, [circleId]);
        Assert.That(accept.IsSuccessStatusCode, Is.True);

        var review = await owner.Network.MarkReviewed(reader.OdinId);
        Assert.That(review.IsSuccessStatusCode, Is.True);
    }

    private static async Task DisconnectReaders(OwnerApiClientRedux owner, params OwnerApiClientRedux[] readers)
    {
        foreach (var reader in readers)
        {
            await owner.Connections.DisconnectFrom(reader.OdinId);
            await reader.Connections.DisconnectFrom(owner.OdinId);
        }
    }

    private static PermissionSetGrantRequest ReadGrant(TargetDrive drive)
    {
        return new PermissionSetGrantRequest
        {
            Drives =
            [
                new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = drive,
                        Permission = DrivePermission.Read
                    }
                }
            ]
        };
    }

    private static async Task<GuestApiClientFactory> RegisterYouAuthDomain(OwnerApiClientRedux owner, TargetDrive drive, AsciiDomainName domain)
    {
        var circleId = Guid.NewGuid();
        await owner.Network.CreateCircle(circleId, "YouAuth readers", ReadGrant(drive));

        var registerDomain = await owner.YouAuth.RegisterDomain(domain, [circleId]);
        Assert.That(registerDomain.IsSuccessStatusCode, Is.True);

        var registerClient = await owner.YouAuth.RegisterClient(domain, "named identity acl test");
        Assert.That(registerClient.IsSuccessStatusCode, Is.True);

        var cat = ClientAccessToken.FromPortableBytes(registerClient.Content.Data);
        return new GuestApiClientFactory(cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
    }

    private static async Task<ExternalFileIdentifier> Upload(OwnerApiClientRedux owner, TargetDrive drive, AccessControlList acl)
    {
        var response = await owner.DriveRedux.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100, acl: acl));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content.File;
    }

    private static async Task<bool> CanGetOverPeer(OwnerApiClientRedux reader, OwnerApiClientRedux owner, ExternalFileIdentifier file)
    {
        var response = await reader.PeerQuery.GetFileHeader(new TransitExternalFileIdentifier
        {
            OdinId = owner.OdinId,
            File = file
        });

        if (response.IsSuccessStatusCode)
        {
            Assert.That(response.Content?.FileId, Is.EqualTo(file.FileId));
            return true;
        }

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "a refused read is a 403, not some other failure");
        return false;
    }

    private static async Task<List<Guid>> QueryOverPeer(OwnerApiClientRedux reader, OwnerApiClientRedux owner, TargetDrive drive)
    {
        var query = QueryDrive(drive);
        var response = await reader.PeerQuery.GetBatch(new PeerQueryBatchRequest
        {
            OdinId = owner.OdinId,
            QueryParams = query.QueryParams,
            ResultOptionsRequest = query.ResultOptionsRequest
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer query failed: {response.StatusCode}");
        return response.Content.SearchResults.Select(r => r.FileId).ToList();
    }

    private static QueryBatchRequest QueryDrive(TargetDrive drive)
    {
        return new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
        };
    }

    private static async Task<bool> FeedHasPost(OwnerApiClientRedux follower, GlobalTransitIdFileIdentifier post)
    {
        await follower.DriveRedux.ProcessInbox(WellKnownAppDrives.FeedDrive);
        var response = await follower.DriveRedux.QueryByGlobalTransitId(new GlobalTransitIdFileIdentifier
        {
            GlobalTransitId = post.GlobalTransitId,
            TargetDrive = WellKnownAppDrives.FeedDrive
        });

        Assert.That(response.IsSuccessStatusCode, Is.True);
        return response.Content.SearchResults.Any();
    }
}
