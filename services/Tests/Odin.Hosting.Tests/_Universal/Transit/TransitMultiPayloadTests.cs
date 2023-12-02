using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;

namespace Odin.Hosting.Tests._Universal.Transit;

public class TransitMultiPayloadTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    public static IEnumerable TestCases()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
    }

    // [Test]
    // [TestCaseSource(nameof(TestCases))]
    // public async Task TransitSendsMultiplePayloads_When_SentViaDriveUpload(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    // {
    // }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task TransitSendsAppNotification(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        /*
         * I need to connect two hobbits
         * sam sends a chat to frodo and includes an app notification
         * the notification should be queued in sam's outbox
         * i call process notifications on sam's owner api
         * the notification will then exist in frodo's inbox
         * i call process notifications on frodo's owner api
         * here we ignore whether or not the push actually went out (because that's a whole other set of dependencies)
         * the notification will then exist in frodo's notification's list
         */

        //Create two connected hobbits

        var targetDrive = callerContext.TargetDrive;
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        await ownerSam.DriveManager.CreateDrive(targetDrive, "Drive 1 Test", "", false);

        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);
        await ownerFrodo.DriveManager.CreateDrive(targetDrive, "Drive 1 Test", "", false);

        // Prepare the app
        Guid appId = Guid.NewGuid();
        var permissions = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Write
                    }
                }
            },
            PermissionSet = new PermissionSet(new List<int>()) //TODO: add permissions for sending notifications?
        };

        var circles = new List<Guid>();
        var circlePermissions = new PermissionSetGrantRequest();
        await ownerSam.AppManager.RegisterApp(appId, permissions, circles, circlePermissions);

        var (appToken, appSharedSecret) = await ownerSam.AppManager.RegisterAppClient(appId);

        Assert.Inconclusive("TODO: wip");
        // await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, circlesForSam);
        // await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, circlesForFrodo);
    }
}