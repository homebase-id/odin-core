using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage.Cache;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Capi;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Authentication.Peer;

#nullable enable

public class PeerCapiAuthenticationHandler(
    IOptionsMonitor<PeerCapiAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    OdinConfiguration config,
    UrlEncoder encoder,
    ICorrelationContext correlationContext,
    ITenantLevel2Cache<PeerCapiAuthenticationHandler> cache,
    IDynamicHttpClientFactory httpClientFactory,
    OdinIdentity odinIdentity)
    : AuthenticationHandler<PeerCapiAuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var capiSession = Context.Request.Headers[ICapiCallbackSession.SessionHttpHeaderName].ToString();
        var capiRemoteDomainAndSessionId = capiSession.Split('~');
        if (capiRemoteDomainAndSessionId.Length != 2)
        {
            return AuthenticateResult.Fail($"Invalid or missing {ICapiCallbackSession.SessionHttpHeaderName}");
        }

        var remoteDomain = capiRemoteDomainAndSessionId[0];
        if (string.IsNullOrWhiteSpace(remoteDomain))
        {
            return AuthenticateResult.Fail($"Invalid sender domain in {ICapiCallbackSession.SessionHttpHeaderName}");
        }

        var sessionId = capiRemoteDomainAndSessionId[1];
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return AuthenticateResult.Fail($"Invalid session id in {ICapiCallbackSession.SessionHttpHeaderName}");
        }

        var sessionLookup = await cache.TryGetAsync<bool>(sessionId);
        if (!sessionLookup.HasValue)
        {
            var localDomainAndSessionId = $"{DnsConfigurationSet.PrefixCertApi}.{odinIdentity.PrimaryDomain}~{sessionId}";
            var url = $"https://{remoteDomain}:{config.Host.DefaultHttpsPort}{UnifiedApiRouteConstants.Capi}/validate/{localDomainAndSessionId}";
            var httpClient = httpClientFactory.CreateClient($"capi-session-validate:{remoteDomain}", cfg =>
            {
                cfg.AllowUntrustedServerCertificate = config.CertificateRenewal.UseCertificateAuthorityProductionServers == false;
            });
            httpClient.DefaultRequestHeaders.Add(ICapiCallbackSession.SessionHttpHeaderName, "here-comes-the-callback");
            httpClient.DefaultRequestHeaders.Add(OdinHeaderNames.CorrelationId, correlationContext.Id);

            var response = await httpClient.GetAsync(url);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return AuthenticateResult.Fail(responseContent);
            }

            await cache.SetAsync(sessionId, true, config.Host.CapiSessionLifetime * 2);
        }

        var claims = new List<Claim>
        {
            new (ClaimTypes.NameIdentifier, remoteDomain, ClaimValueTypes.String, Options.ClaimsIssuer),
            new (ClaimTypes.Name, remoteDomain, ClaimValueTypes.String, Options.ClaimsIssuer),
            new (OdinClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
            new (OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.YouFoundationIssuer),
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, Scheme.Name)
        );

        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}