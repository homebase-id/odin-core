using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests._Universal.Owner.Apps;

/// <summary>
/// Cat 3.1-3.5: an app declares its default circles at registration, and the deposit-only invariant
/// is enforced when the definition is written.
/// </summary>
public class AppDefaultCircleTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Frodo });
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

    [Test]
    public async Task RegisteringAnAppCreatesItsDeclaredCircles()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var (appId, drive) = await PrepareAppDrive(frodo);

        var circleId = Guid.NewGuid();
        var response = await frodo.AppManager.RegisterApp(appId, AppPermissions(drive),
            defaultCircles:
            [
                new AppDefaultCircleRequest
                {
                    Id = circleId,
                    Name = "Chat-only",
                    GrantOn = CircleGrantOn.Connect,
                    Designation = CircleDesignation.Personal,
                    Emoji = "💬",
                    DriveGrants = [DepositGrant(drive)]
                }
            ]);

        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        var circles = await frodo.Network.GetCircleDefinitions(includeSystemCircle: false);
        var created = circles.Content.SingleOrDefault(c => c.Id == circleId);

        ClassicAssert.IsNotNull(created, "the declared circle should exist as a real row");
        ClassicAssert.AreEqual("Chat-only", created.Name);
        ClassicAssert.AreEqual(appId, created.AppId, "the circle should be owned by the declaring app");
        ClassicAssert.AreEqual(CircleGrantOn.Connect, created.GrantOn);
        ClassicAssert.AreEqual("💬", created.Emoji);
    }

    [Test]
    public async Task ReRegisteringAnAppUpdatesItsCirclesRatherThanDuplicating()
    {
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var (appId, drive) = await PrepareAppDrive(frodo);

        var circleId = Guid.NewGuid();

        await frodo.AppManager.RegisterApp(appId, AppPermissions(drive),
            defaultCircles: [Declared(circleId, "First name", drive)]);

        await frodo.AppManager.RegisterApp(appId, AppPermissions(drive),
            defaultCircles: [Declared(circleId, "Second name", drive)]);

        var circles = await frodo.Network.GetCircleDefinitions(includeSystemCircle: false);
        var matching = circles.Content.Where(c => c.Id == circleId).ToList();

        ClassicAssert.AreEqual(1, matching.Count, "matching on circle id must not duplicate the row");
        ClassicAssert.AreEqual("Second name", matching[0].Name, "the row should have been updated");
    }

    [Test]
    public async Task AnAmbientCircleCannotCarryPermissionKeys()
    {
        // Identity-wide keys are only mintable at the review; a circle that enrols without the owner
        // present must not hand one out.
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var (appId, drive) = await PrepareAppDrive(frodo);

        var request = new AppRegistrationRequest
        {
            AppId = appId,
            Name = $"Test_{appId}",
            PermissionSet = AppPermissions(drive).PermissionSet,
            Drives = AppPermissions(drive).Drives?.ToList(),
            DefaultCircles =
            [
                new AppDefaultCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Sneaky",
                    GrantOn = CircleGrantOn.Connect,
                    DriveGrants = [DepositGrant(drive)],
                    Permissions = new PermissionSet(PermissionKeys.AllowIntroductions)
                }
            ]
        };

        var response = await frodo.AppManager.TryRegisterApp(request);

        ClassicAssert.IsFalse(response.IsSuccessStatusCode,
            "a grant-on-connect circle carrying a permission key must be rejected");
        ClassicAssert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Test]
    public async Task AnAmbientCircleCannotGrantReadOnANonPublicDrive()
    {
        // Read grants carry a storage key. Deposit-only means there are none.
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var (appId, drive) = await PrepareAppDrive(frodo);

        var request = new AppRegistrationRequest
        {
            AppId = appId,
            Name = $"Test_{appId}",
            PermissionSet = AppPermissions(drive).PermissionSet,
            Drives = AppPermissions(drive).Drives?.ToList(),
            DefaultCircles =
            [
                new AppDefaultCircleRequest
                {
                    Id = Guid.NewGuid(),
                    Name = "Reader",
                    GrantOn = CircleGrantOn.Connect,
                    DriveGrants =
                    [
                        new DriveGrantRequest
                        {
                            PermissionedDrive = new PermissionedDrive
                            {
                                Drive = drive,
                                Permission = DrivePermission.Read | DrivePermission.Write
                            }
                        }
                    ]
                }
            ]
        };

        var response = await frodo.AppManager.TryRegisterApp(request);

        ClassicAssert.IsFalse(response.IsSuccessStatusCode,
            "a grant-on-connect circle granting read on a private drive must be rejected");
        ClassicAssert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Test]
    public async Task AReviewCircleMayCarryPermissionKeysAndReadGrants()
    {
        // The review is the key ceremony: everything the ambient invariant forbids is allowed here.
        var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var (appId, drive) = await PrepareAppDrive(frodo);

        var circleId = Guid.NewGuid();
        var response = await frodo.AppManager.RegisterApp(appId, AppPermissions(drive),
            defaultCircles:
            [
                new AppDefaultCircleRequest
                {
                    Id = circleId,
                    Name = "Introductions",
                    GrantOn = CircleGrantOn.Review,
                    Permissions = new PermissionSet(PermissionKeys.AllowIntroductions),
                    DriveGrants =
                    [
                        new DriveGrantRequest
                        {
                            PermissionedDrive = new PermissionedDrive
                            {
                                Drive = drive,
                                Permission = DrivePermission.Read | DrivePermission.Write
                            }
                        }
                    ]
                }
            ]);

        ClassicAssert.IsTrue(response.IsSuccessStatusCode,
            $"a review circle should accept keys and reads: {response.Error?.Content}");

        var circles = await frodo.Network.GetCircleDefinitions(includeSystemCircle: false);
        var created = circles.Content.SingleOrDefault(c => c.Id == circleId);

        ClassicAssert.IsNotNull(created);
        ClassicAssert.AreEqual(CircleGrantOn.Review, created.GrantOn);
    }

    private static AppDefaultCircleRequest Declared(Guid circleId, string name, TargetDrive drive)
    {
        return new AppDefaultCircleRequest
        {
            Id = circleId,
            Name = name,
            GrantOn = CircleGrantOn.Connect,
            DriveGrants = [DepositGrant(drive)]
        };
    }

    private static DriveGrantRequest DepositGrant(TargetDrive drive)
    {
        return new DriveGrantRequest
        {
            PermissionedDrive = new PermissionedDrive
            {
                Drive = drive,
                Permission = DrivePermission.Write | DrivePermission.React
            }
        };
    }

    private static PermissionSetGrantRequest AppPermissions(TargetDrive drive)
    {
        return new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections),
            Drives = [DepositGrant(drive)]
        };
    }

    private static async Task<(Guid appId, TargetDrive drive)> PrepareAppDrive(
        ApiClient.Owner.OwnerApiClientRedux frodo)
    {
        var appId = Guid.NewGuid();
        var drive = TargetDrive.NewTargetDrive();

        await frodo.DriveManager.CreateDrive(drive, $"drive for {appId}", "", allowAnonymousReads: false);

        return (appId, drive);
    }
}
