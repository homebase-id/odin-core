using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Onboarding;
using Odin.Services.Drives;
using Odin.Services.Registry;
using Refit;

namespace Odin.Hosting.Tests.V2.Onboarding;

/// <summary>
/// GET /api/v2/onboarding/invited-by: the owner reads who invited the identity off its registration (#1908).
/// </summary>
[TestFixture]
public class V2OnboardingTests : V2Fixture
{
    public interface IOnboardingHttpClientApiV2
    {
        [Get(UnifiedApiRouteConstants.Onboarding + "/invited-by")]
        Task<ApiResponse<V2OnboardingController.InvitedByResponse>> GetInvitedBy();
    }

    private IIdentityRegistry Registry => Host.Server.Services.GetRequiredService<IIdentityRegistry>();

    [Test]
    public async Task TheOwnerReadsWhoInvitedTheIdentity()
    {
        var owner = await LoginAsOwner();

        // The registry is not reset between tests, so put the registration back as it was
        var registration = await Registry.GetAsync(owner.Identity.DomainName);
        var original = registration.Invitation;
        registration.Invitation = new RegistrationInvitation("code-1", "inviter.dotyou.cloud", "test");

        try
        {
            var response = await GetInvitedByAsync(owner);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content!.InvitedBy, Is.EqualTo("inviter.dotyou.cloud"));
        }
        finally
        {
            registration.Invitation = original;
        }
    }

    [Test]
    public async Task AnIdentityWithoutAnInvitationHasNoInviter()
    {
        var owner = await LoginAsOwner();

        var response = await GetInvitedByAsync(owner);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.InvitedBy, Is.Null);
    }

    [Test]
    public async Task AnAppCannotReadTheInviter()
    {
        var app = await SetupCaller(CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read));

        var response = await GetInvitedByAsync(app);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static Task<ApiResponse<V2OnboardingController.InvitedByResponse>> GetInvitedByAsync(IV2Caller caller)
    {
        var http = caller.Factory.CreateHttpClient(caller.Identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<IOnboardingHttpClientApiV2>(http, sharedSecret).GetInvitedBy();
    }
}
