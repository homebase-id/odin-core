using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Mail;

/// <summary>
/// GET /api/v2/mail/status is the one mail endpoint with no drive gate. The app has to render
/// "this server has no email" before it owns a drive, and has to tell that apart from "you
/// haven't set it up yet" — so status must answer for a caller holding nothing.
/// </summary>
public class V2MailStatusTests : V2Fixture
{
    [Test]
    public async Task StatusAnswersAnAppWithNoEmailDriveAtAll()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var someDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(someDrive, "Some Drive", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(owner, someDrive, DrivePermission.ReadWrite);

        var response = await new V2MailClient(app.Identity, app.Factory).GetStatusAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var status = response.Content;
        Assert.That(status, Is.Not.Null);

        // The flag is off on every host today; this is exactly the "no email here" screen.
        Assert.That(status!.TenantMailEnabled, Is.False);
        Assert.That(status.DriveProvisioned, Is.False);
        Assert.That(status.MailboxProvisioned, Is.False);
        Assert.That(status.Activated, Is.False);
        Assert.That(status.PrimaryEmailAddress, Is.Null);
        Assert.That(status.CurrentKeyFileUniqueId, Is.Null);
    }

    [Test]
    public async Task StatusReportsTheDriveOnceTheAppCanUseIt()
    {
        var caller = await SetupCaller(CallerSpec.App(
            new DriveSpec(WellKnownAppDrives.EmailAppDrive, "Email", AllowAnonymousReads: false, OwnerOnly: true),
            DrivePermission.ReadWrite));

        var response = await new V2MailClient(caller.Identity, caller.Factory).GetStatusAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.DriveProvisioned, Is.True);

        // Setup has gone no further than the drive.
        Assert.That(response.Content.MailboxProvisioned, Is.False);
        Assert.That(response.Content.Activated, Is.False);
    }

    /// <summary>
    /// Half a grant does not make an email app — status must agree with the gate rather than
    /// telling the client it is provisioned and then refusing every action with a 403.
    /// </summary>
    [Test]
    public async Task StatusDoesNotReportTheDriveOnAPartialGrant()
    {
        var caller = await SetupCaller(CallerSpec.App(
            new DriveSpec(WellKnownAppDrives.EmailAppDrive, "Email", AllowAnonymousReads: false, OwnerOnly: true),
            DrivePermission.Read));

        var response = await new V2MailClient(caller.Identity, caller.Factory).GetStatusAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.DriveProvisioned, Is.False);
    }

    [Test]
    public async Task StatusIsReachableByTheOwner()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await new V2MailClient(owner.Identity, owner.Factory).GetStatusAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.TenantMailEnabled, Is.False);
    }
}
