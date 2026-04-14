using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage.Cache;
using Odin.Core.Util;
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
    ILoggerFactory loggerFactory,
    OdinConfiguration config,
    UrlEncoder encoder,
    ICorrelationContext correlationContext,
    ITenantLevel2Cache<PeerCapiAuthenticationHandler> cache,
    IDynamicHttpClientFactory httpClientFactory,
    OdinIdentity odinIdentity,
    IHostApplicationLifetime applicationLifetime)
    : AuthenticationHandler<PeerCapiAuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

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
            httpClient.DefaultRequestHeaders.Add(ICapiCallbackSession.SessionHttpHeaderName, "here-comes-the-callback"); // Required dummy value
            httpClient.DefaultRequestHeaders.Add(OdinHeaderNames.CorrelationId, correlationContext.Id);

            HttpResponseMessage response;
            try
            {
                response = await TryRetry.Create()
                    .WithAttempts(3)
                    .WithDelay(TimeSpan.FromSeconds(1))
                    .WithCancellation(applicationLifetime.ApplicationStopping)
                    .RetryOnPredicate((ex, _) => ex is HttpRequestException httpEx && httpEx.Message.Contains("Connection refused"))
                    .ExecuteAsync(async () => await httpClient.GetAsync(url, applicationLifetime.ApplicationStopping));
            }
            catch (OperationCanceledException ex)
            {
                return AuthenticateResult.Fail(ex.Message);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("SSL connection could not be established"))
            {
                // Silently catch SSL errors and return an authentication failure,
                // as this likely indicates a missing or an untrusted certificate on the remote peer.
                return AuthenticateResult.Fail(ex.Message);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("Connection refused"))
            {
                // Silently catch connection refused errors and return an authentication failure.
                // The remote is gone and no point letting it bubble up to log an error we can't do anything about.
                var logger = _loggerFactory.CreateLogger<PeerCapiAuthenticationHandler>();
                logger.LogInformation("PeerCapi authentication failed: connection refused");
                return AuthenticateResult.Fail(ex.Message);
            }

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