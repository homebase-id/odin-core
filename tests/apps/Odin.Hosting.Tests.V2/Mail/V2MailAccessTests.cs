using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Mail;

/// <summary>
/// The access-control matrix for /api/v2/mail, exercised with <c>Email:TenantMail:Enabled</c>
/// OFF — which is how it is on every host today and must stay until MX nodes exist.
///
/// That works because of the order the gate runs in: policy (403 for a guest, which holds a
/// valid token but not an owner/app one), then the drive exists (400),
/// then Read+Write on it (403), and only then the feature flag. So a caller that reaches a
/// flag-or-activation 400 has, by construction, passed the gate — the 400 IS the assertion.
///
/// The endpoint under test is POST /challenge: gated on drive access but not on the flag, so it
/// is the one action available to exercise the gate before the setup endpoints land.
/// </summary>
public class V2MailAccessTests : V2Fixture
{
    private static DriveSpec EmailDrive() =>
        new(WellKnownAppDrives.EmailAppDrive, "Email", AllowAnonymousReads: false, OwnerOnly: true);

    public static IEnumerable<object[]> GateCases()
    {
        // Owner holds DrivePermission.All on every drive that exists, so the owner passes.
        yield return [CallerSpec.Owner(EmailDrive()), HttpStatusCode.BadRequest];

        // The email app: Read AND Write on the email drive.
        yield return [CallerSpec.App(EmailDrive(), DrivePermission.ReadWrite), HttpStatusCode.BadRequest];

        // Half the grant is not the grant. Every mail action both writes key material to the
        // drive and reads it back, so neither half alone is enough.
        yield return [CallerSpec.App(EmailDrive(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(EmailDrive(), DrivePermission.Write), HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(GateCases))]
    public async Task ChallengeEnforcesReadWriteOnTheEmailDrive(CallerSpec spec, HttpStatusCode expected)
    {
        var caller = await SetupCaller(spec);
        var mail = new V2MailClient(caller.Identity, caller.Factory);

        var response = await mail.CreateChallengeAsync();

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    /// <summary>
    /// The truest "no grant" case: a legitimate app on this identity, holding Read+Write on a
    /// drive of its own, naming the mail API. Access to some drive is not access to THE drive.
    /// </summary>
    [Test]
    public async Task AppGrantedOnAnotherDriveIsForbidden()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        // The email drive exists, so this cannot pass for the "drive missing" reason.
        await owner.Admin.CreateDrive(WellKnownAppDrives.EmailAppDrive, "Email",
            allowAnonymousReads: false, ownerOnly: true);

        var otherDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(otherDrive, "Some Other Drive", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(owner, otherDrive, DrivePermission.ReadWrite);

        var mail = new V2MailClient(app.Identity, app.Factory);
        var response = await mail.CreateChallengeAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// Before the owner approves the app's drive request there is no drive to hold a grant on.
    /// That is a 400, not a 403: nothing is being refused, the setup step simply has not happened.
    /// </summary>
    [Test]
    public async Task ChallengeWithoutAnEmailDriveIsABadRequest()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var mail = new V2MailClient(owner.Identity, owner.Factory);

        var response = await mail.CreateChallengeAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// A connected guest holds a valid token, so it authenticates and then fails the OwnerOrApp
    /// policy — 403, not 401. The rejection happens before any drive logic, so the drive here is
    /// irrelevant. Mail is the identity owner's own setup surface; no peer has business in it.
    /// </summary>
    [Test]
    public async Task GuestsCannotReachTheMailApiAtAll()
    {
        var caller = await SetupCaller(CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read));
        var mail = new V2MailClient(caller.Identity, caller.Factory);

        var response = await mail.CreateChallengeAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
