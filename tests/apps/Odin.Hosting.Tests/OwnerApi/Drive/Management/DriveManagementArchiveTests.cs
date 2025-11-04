using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.OwnerApi.Drive.Management;

public class DriveManagementArchiveTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo]);
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
        _scaffold.AssertLogEvents();
    }

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), true };
    }

    public static IEnumerable AppAllowed()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), false };
    }

    public static IEnumerable GuestAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), false };
    }

    [Test, Explicit]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanArchiveDriveAndDriveListIsCorrectlyReturned(IApiClientContext callerContext, bool shouldHaveArchivedDrive)
    {
        // Prepare
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var driveType = callerContext.TargetDrive.Type;
        var drive1 = callerContext.TargetDrive;
        var drive2 = TargetDrive.NewTargetDrive(driveType);

        await ownerApiClient.DriveManager.CreateDrive(drive1, "drive 1", "", false);
        await ownerApiClient.DriveManager.CreateDrive(drive2, "drive 2", "", false);

        await callerContext.Initialize(ownerApiClient);
        var driveApiClient = new UniversalDriveApiClient(ownerApiClient.OdinId, callerContext.GetFactory());

        var drivesByTypeResponse = await driveApiClient.GetDrivesByType(driveType);
        ClassicAssert.IsTrue(drivesByTypeResponse.IsSuccessStatusCode);
        var drivesByType = drivesByTypeResponse.Content;
        ClassicAssert.IsTrue(drivesByType.Results.Any(p => p.TargetDrive == drive1));
        ClassicAssert.IsTrue(drivesByType.Results.Any(p => p.TargetDrive == drive2));

        // Act - set archive on drive 1
        var setFlagResponse = await ownerApiClient.DriveManager.SetArchiveFlag(drive1, true);
        ClassicAssert.IsTrue(setFlagResponse.IsSuccessStatusCode);

        //
        // Assert
        //
        var updatedDrivesByTypeResponse = await driveApiClient.GetDrivesByType(driveType);
        ClassicAssert.IsTrue(updatedDrivesByTypeResponse.IsSuccessStatusCode);
        var updatedDrivesList = updatedDrivesByTypeResponse.Content;

        if (shouldHaveArchivedDrive)
        {
            ClassicAssert.IsTrue(updatedDrivesList.Results.Any(p => p.TargetDrive == drive1), "drive 1 should be IN the list");
        }
        else
        {
            ClassicAssert.IsTrue(updatedDrivesList.Results.All(p => p.TargetDrive != drive1), "drive 1 should not be in the list");
        }

        ClassicAssert.IsTrue(updatedDrivesList.Results.Any(p => p.TargetDrive == drive2));

        await callerContext.Cleanup();
    }

    [Test, Explicit]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanUnarchiveDriveAndDriveReturnedFromResults(IApiClientContext callerContext, bool shouldHaveArchivedDrive)
    {
        // Prepare
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var driveType = callerContext.TargetDrive.Type;
        var drive1 = callerContext.TargetDrive;
        var drive2 = TargetDrive.NewTargetDrive(driveType);

        await ownerApiClient.DriveManager.CreateDrive(drive1, "drive 1", "", false);
        await ownerApiClient.DriveManager.CreateDrive(drive2, "drive 2", "", false);

        await callerContext.Initialize(ownerApiClient);
        var driveApiClient = new UniversalDriveApiClient(ownerApiClient.OdinId, callerContext.GetFactory());

        var drivesByTypeResponse = await driveApiClient.GetDrivesByType(driveType);
        var drivesByType = drivesByTypeResponse.Content;
        ClassicAssert.IsTrue(drivesByType.Results.Any(p => p.TargetDrive == drive1));
        ClassicAssert.IsTrue(drivesByType.Results.Any(p => p.TargetDrive == drive2));

        // Act - set archive on drive 1
        var setFlagResponse = await ownerApiClient.DriveManager.SetArchiveFlag(drive1, true);
        ClassicAssert.IsTrue(setFlagResponse.IsSuccessStatusCode);

        // now set unarhive and ensure we can see it


        var unarchiveDriveResponse = await ownerApiClient.DriveManager.SetArchiveFlag(drive1, false);
        ClassicAssert.IsTrue(unarchiveDriveResponse.IsSuccessStatusCode);

        //
        // Assert
        //
        var updatedDrivesByTypeResponse = await driveApiClient.GetDrivesByType(driveType);
        ClassicAssert.IsTrue(updatedDrivesByTypeResponse.IsSuccessStatusCode);
        var updatedDrivesList = updatedDrivesByTypeResponse.Content;

        ClassicAssert.IsTrue(updatedDrivesList.Results.Any(p => p.TargetDrive == drive1), "drive 1 should be IN the list");
        ClassicAssert.IsTrue(updatedDrivesList.Results.Any(p => p.TargetDrive == drive2));

        await callerContext.Cleanup();
    }

    [Test]
    public async Task FailToArchiveSystemDrive()
    {
        // Prepare
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        // Act - set archive on drive 1
        foreach(var drive in SystemDriveConstants.SystemDrives)
        {
            var setFlagResponse = await ownerApiClient.DriveManager.SetArchiveFlag(drive, true);
            ClassicAssert.IsTrue(setFlagResponse.StatusCode == HttpStatusCode.BadRequest);
        }
    }
}