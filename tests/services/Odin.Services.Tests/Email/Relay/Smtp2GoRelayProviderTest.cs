#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Http;
using Odin.Core.Util;
using Odin.Services.Configuration;
using Odin.Services.Email.Relay;

namespace Odin.Services.Tests.Email.Relay;

/// <summary>
/// How the relay's refusals reach us. The job decides retry-or-stop from this, so the HTTP
/// status has to survive into the exception rather than living only in its message.
/// </summary>
public class Smtp2GoRelayProviderTest
{
    private static readonly AsciiDomainName Domain = new("frodo.example.com");

    // The body SMTP2GO actually returned on 2026-10-07, trimmed
    private const string PlanLimitBody =
        """{"request_id":"x","data":{"error_code":"E_ApiResponseCodes.API_EXCEPTION","error":"Free plans can add a maximum of 5 verified senders, please upgrade to a paid plan to add more"}}""";

    private const string UnknownKeyBody =
        """{"request_id":"x","data":{"error_code":"E_ApiResponseCodes.API_EXCEPTION","error":"An API User matching the passed 'api_key' was not found"}}""";

    private const string NoDomainsBody = """{"request_id":"x","data":{"domains":[]}}""";

    [Test]
    public void ItShouldReportARefusalAsPermanentWithItsStatusAndTheRelaysWords()
    {
        // /domain/view finds nothing, then /domain/add is refused
        var provider = CreateProvider(path => path.EndsWith("/domain/view")
            ? (HttpStatusCode.OK, NoDomainsBody)
            : (HttpStatusCode.BadRequest, PlanLimitBody));

        var e = Assert.ThrowsAsync<MailRelayException>(() => provider.EnsureDomainAsync(Domain, CancellationToken.None));

        Assert.That(e!.StatusCode, Is.EqualTo(400), $"status was {e.StatusCode}");
        Assert.That(e.IsPermanent, Is.True);
        Assert.That(e.Message, Does.Contain("maximum of 5 verified senders"), $"message was: {e.Message}");
    }

    /// <summary>
    /// SMTP2GO answers an unknown API key with a 500. That is exactly what a key rotation looks
    /// like for a moment, so it must stay retryable rather than abort onboarding.
    /// </summary>
    [Test]
    public void ItShouldReportAServerErrorAsTransient()
    {
        var provider = CreateProvider(_ => (HttpStatusCode.InternalServerError, UnknownKeyBody));

        var e = Assert.ThrowsAsync<MailRelayException>(() => provider.GetDomainAsync(Domain, CancellationToken.None));

        Assert.That(e!.StatusCode, Is.EqualTo(500), $"status was {e.StatusCode}");
        Assert.That(e.IsPermanent, Is.False);
    }

    [TestCase(408)]
    [TestCase(429)]
    public void ItShouldTreatTimeoutAndRateLimitAsTransient(int status)
    {
        var e = new MailRelayException("Relay: POST /domain/add returned " + status, status);
        Assert.That(e.IsPermanent, Is.False, $"{status} was treated as permanent");
    }

    /// <summary>
    /// Activation's job and the owner's repair button can both see "not registered" and both
    /// add; the loser gets a 400 "already exists". That is a won race, not a refusal.
    /// </summary>
    [Test]
    public async Task ItShouldTreatLosingTheAddRaceAsRegistered()
    {
        var views = 0;
        var provider = CreateProvider(path =>
        {
            if (path.EndsWith("/domain/view"))
            {
                return views++ == 0 ? (HttpStatusCode.OK, NoDomainsBody) : (HttpStatusCode.OK, RegisteredBody);
            }
            return (HttpStatusCode.BadRequest, AlreadyExistsBody);
        });

        var state = await provider.EnsureDomainAsync(Domain, CancellationToken.None);

        Assert.That(state.Domain, Is.EqualTo(Domain.DomainName), $"domain was '{state.Domain}'");
        Assert.That(views, Is.EqualTo(2), $"/domain/view was called {views} time(s)");
    }

    private const string AlreadyExistsBody =
        """{"request_id":"x","data":{"error_code":"E_ApiResponseCodes.API_EXCEPTION","error":"A sender domain matching the passed value of frodo.example.com already exists"}}""";

    private const string RegisteredBody = """
    {"request_id":"x","data":{"domains":[{"domain":{
      "fulldomain":"frodo.example.com","subdomain":"frodo","domain":"example","suffix":"com",
      "dkim_expected":"dkim.smtp2go.net","dkim_selector":"s934313","dkim_verified":false,"dkim_status":"","dkim_value":"",
      "rpath_expected":"return.smtp2go.net","rpath_selector":"em934313","rpath_verified":false,"rpath_status":"","rpath_value":""},
      "trackers":[]}]}}
    """;

    //

    private static Smtp2GoRelayProvider CreateProvider(Func<string, (HttpStatusCode status, string body)> responder)
    {
        var httpClientFactory = new Mock<IDynamicHttpClientFactory>();
        httpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>(), It.IsAny<Action<ClientHandlerConfig>?>()))
            .Returns(() => new HttpClient(new ScriptedHandler(responder)));

        return new Smtp2GoRelayProvider(
            new Mock<ILogger<Smtp2GoRelayProvider>>().Object,
            new OdinConfiguration
            {
                Email = new OdinConfiguration.EmailSection
                {
                    Relay = new OdinConfiguration.RelaySection
                    {
                        Provider = OdinConfiguration.RelayProvider.Smtp2Go,
                        ApiKey = "test-key",
                        ApiBaseUrl = "https://relay.test/v3",
                    },
                },
            },
            httpClientFactory.Object);
    }

    private class ScriptedHandler(Func<string, (HttpStatusCode status, string body)> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = responder(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
