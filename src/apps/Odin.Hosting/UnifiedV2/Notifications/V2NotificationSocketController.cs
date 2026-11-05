#nullable enable

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Odin.Core.Exceptions;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication;
using Odin.Services.AppNotifications.Push;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Peer.AppNotification;

[assembly: InternalsVisibleTo("Odin.Hosting.Tests")]
[assembly: InternalsVisibleTo("Odin.Hosting.Tests.V2")]

namespace Odin.Hosting.UnifiedV2.Notifications
{
    /// <summary>
    /// Cookie-less WebSocket entry point for clients (e.g. WASM, native) that cannot send
    /// the app/owner auth cookie on the upgrade. The client authenticates by offering its
    /// bearer token as a value in the Sec-WebSocket-Protocol header, prefixed with
    /// "odin.bearer." — the Kubernetes-style subprotocol bearer pattern. The controller
    /// resolves it to an IOdinContext before accepting the upgrade; once the WS is open
    /// the protocol is the same as the cookie-authenticated route.
    /// </summary>
    /// <remarks>
    /// Browsers with no bearer token (the owner console, an app holding only its cookie) use the cookie routes here:
    /// one route per cookie, as V1 has, because an app served from the identity's own host sends the owner cookie
    /// too, and <see cref="UnifiedAuthenticationHandler"/> would pick that one. So they read their own cookie rather
    /// than run that handler. Unlike V1 they check the Origin; see <see cref="IsAllowedCookieOrigin"/>.
    /// </remarks>
    [ApiController]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2NotificationSocketController : OdinControllerBase
    {
        // Prefix on a Sec-WebSocket-Protocol value that carries the base64 ClientAuthenticationToken.
        // Mirrors the kube-apiserver "base64url.bearer.authorization.k8s.io.<token>" pattern.
        private const string BearerProtocolPrefix = "odin.bearer.";

        // Application-level subprotocol the server commits to. Echoed back to the client so
        // browsers don't reject the 101 response.
        private const string NegotiatedSubProtocol = "odin.notify.v1";

        private readonly ILogger<V2NotificationSocketController> _logger;
        private readonly AppNotificationHandler _notificationHandler;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IAppRegistrationService _appRegistrationService;
        private readonly PeerAppNotificationService _peerAppNotificationService;

        public V2NotificationSocketController(
            ILogger<V2NotificationSocketController> logger,
            AppNotificationHandler notificationHandler,
            IHostApplicationLifetime hostApplicationLifetime,
            IAppRegistrationService appRegistrationService,
            PeerAppNotificationService peerAppNotificationService)
        {
            _logger = logger;
            _notificationHandler = notificationHandler;
            _hostApplicationLifetime = hostApplicationLifetime;
            _appRegistrationService = appRegistrationService;
            _peerAppNotificationService = peerAppNotificationService;
        }

        [HttpGet]
        [Route(UnifiedApiRouteConstants.NotifySocket)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public Task Connect()
        {
            return HandleAsync();
        }

        // Browser/WASM clients upgrade over HTTP/2 Extended CONNECT (RFC 8441:
        // :method=CONNECT, :protocol=websocket). MVC routing rejects those with
        // 405 Allow: GET when only [HttpGet] is present, so the wasm-facing route
        // accepts both verbs. Native HTTP/1.1 clients keep using the [HttpGet]
        // route above — switching that route to AcceptVerbs caused 401s on
        // Ktor CIO/OkHttp/Darwin (the bearer subprotocol value got dropped
        // before the controller ran).
        [AcceptVerbs("GET", "CONNECT")]
        [Route(UnifiedApiRouteConstants.NotifySocketWasm)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public Task ConnectWasm()
        {
            return HandleAsync();
        }

        /// <summary>The owner console's socket: the owner cookie only, as V1's <c>/api/owner/v1/notify/ws</c>.</summary>
        // GET and CONNECT: a browser may upgrade over HTTP/2 Extended CONNECT, as on the wasm route above
        [AcceptVerbs("GET", "CONNECT")]
        [Route(UnifiedApiRouteConstants.NotifySocketOwnerCookie)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task ConnectOwnerCookie()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!IsAllowedCookieOrigin(HttpContext.Request.Headers.Origin, HttpContext.Request.Host, corsHostName: null))
            {
                RefuseCookieOrigin();
                return;
            }

            if (!ClientAuthenticationToken.TryParse(HttpContext.Request.Cookies[OwnerAuthConstants.CookieName], out var token) ||
                token.ClientTokenType != ClientTokenType.Owner)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            try
            {
                var authService = HttpContext.RequestServices.GetRequiredService<OwnerAuthenticationService>();
                var clientContext = new OdinClientContext
                {
                    CorsHostName = null,
                    ClientIdOrDomain = null,
                    AccessRegistrationId = token.Id,
                    DevicePushNotificationKey = PushNotificationCookieUtil.GetDeviceKey(HttpContext.Request)
                };

                if (!await authService.UpdateOdinContextAsync(token, clientContext, WebOdinContext))
                {
                    HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }
            catch (OdinSecurityException e)
            {
                _logger.LogDebug("V2 WS upgrade rejected (401): owner cookie failed: {error}", e.Message);
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // V1's owner handler does this on the same upgrade; the console's socket shouldn't stop being a trigger
            var versionUpgradeScheduler = HttpContext.RequestServices.GetRequiredService<VersionUpgradeScheduler>();
            await versionUpgradeScheduler.EnsureScheduledAsync(token, WebOdinContext);

            await AcceptAndRunAsync(WebOdinContext, RequestedNotifySubProtocol());
        }

        /// <summary>An app's socket by its cookie (see <see cref="PreAuth"/>), as V1's <c>/api/apps/v1/notify/ws</c>.</summary>
        // GET and CONNECT: a browser may upgrade over HTTP/2 Extended CONNECT, as on the wasm route above
        [AcceptVerbs("GET", "CONNECT")]
        [Route(UnifiedApiRouteConstants.NotifySocketAppCookie)]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task ConnectAppCookie()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (!ClientAuthenticationToken.TryParse(HttpContext.Request.Cookies[YouAuthConstants.AppCookieName], out var token))
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            IOdinContext? authenticatedContext;
            try
            {
                authenticatedContext = await ResolveContextAsync(token);
            }
            catch (OdinSecurityException e)
            {
                _logger.LogDebug("V2 WS upgrade rejected (401): app cookie failed: {error}", e.Message);
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (authenticatedContext == null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var corsHostName = authenticatedContext.Caller.OdinClientContext?.CorsHostName;
            if (!IsAllowedCookieOrigin(HttpContext.Request.Headers.Origin, HttpContext.Request.Host, corsHostName))
            {
                RefuseCookieOrigin();
                return;
            }

            await AcceptAndRunAsync(authenticatedContext, RequestedNotifySubProtocol());
        }

        /// <summary>
        /// Sets the app cookie <see cref="ConnectAppCookie"/> reads, from the app's bearer token or, to extend it, the
        /// cookie itself. The V2 twin of V1's <c>/api/apps/v1/notify/preauth</c>. Reads its own token, because the
        /// unified handler would pick an owner cookie over the bearer.
        /// </summary>
        [HttpPost]
        [Route(UnifiedApiRouteConstants.NotifyPreAuth)]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        [ApiExplorerSettings(IgnoreApi = true)]
        public async Task<IActionResult> PreAuth()
        {
            if (!UnifiedAuthenticationHandler.TryGetClientAuthTokenFromHeader(HttpContext, out var token) &&
                !ClientAuthenticationToken.TryParse(HttpContext.Request.Cookies[YouAuthConstants.AppCookieName], out token))
            {
                return Unauthorized();
            }

            try
            {
                if (await ResolveContextAsync(token) == null)
                {
                    return Unauthorized();
                }
            }
            catch (OdinSecurityException)
            {
                return Unauthorized();
            }

            AuthenticationCookieUtil.SetCookie(HttpContext.Response, YouAuthConstants.AppCookieName, token);
            return Ok();
        }

        /// <summary>
        /// Whether a browser page at <paramref name="origin"/> may use a cookie on a socket: the identity's own host,
        /// or the host the owner registered the app on (<paramref name="corsHostName"/>).
        /// </summary>
        /// <remarks>
        /// Against cross-site WebSocket hijacking. V1 has no Origin check: its only guard is that the auth cookies are
        /// SameSite=Strict, which says nothing about a sibling on the same site (<c>evil.example.com</c> against
        /// <c>frodo.example.com</c>). A browser always sends Origin on an upgrade and a page can't change it, so a
        /// missing one is refused too: a client with no browser has a bearer token to use instead.
        /// </remarks>
        internal static bool IsAllowedCookieOrigin(StringValues origin, HostString requestHost, string? corsHostName)
        {
            if (origin.Count != 1 || !Uri.TryCreate(origin[0], UriKind.Absolute, out var originUri) ||
                originUri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            return IsSameHost(originUri, requestHost) ||
                   (!string.IsNullOrEmpty(corsHostName) && IsSameHost(originUri, new HostString(corsHostName)));

            // No port in a host name means the https default, which is what an identity is served on
            static bool IsSameHost(Uri uri, HostString host) =>
                string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == (host.Port ?? 443);
        }

        private void RefuseCookieOrigin()
        {
            _logger.LogWarning("V2 WS upgrade rejected (403): origin [{origin}] may not use a cookie on host {host}",
                HttpContext.Request.Headers.Origin.ToString(), HttpContext.Request.Host);
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        }

        // A browser fails a 101 naming a subprotocol it didn't offer
        private string? RequestedNotifySubProtocol()
        {
            return HttpContext.WebSockets.WebSocketRequestedProtocols.Contains(NegotiatedSubProtocol)
                ? NegotiatedSubProtocol
                : null;
        }

        private async Task HandleAsync()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var bearerProtocol = HttpContext.WebSockets.WebSocketRequestedProtocols
                .FirstOrDefault(p => p.StartsWith(BearerProtocolPrefix, StringComparison.Ordinal));

            if (bearerProtocol == null)
            {
                // Diagnostics for the intermittent native-client 401: log the wire
                // protocol (HTTP/1.1 vs HTTP/2 Extended CONNECT — reveals a proxy
                // forwarding the upgrade over h2) and the requested subprotocols
                // MINUS the bearer (so we never log the credential). If this shows
                // protocols=[odin.notify.v1] with the bearer absent, the bearer was
                // stripped/reordered before reaching the controller (proxy/engine),
                // not a client encoding bug.
                _logger.LogWarning(
                    "V2 WS upgrade rejected (401): no '{prefix}*' subprotocol present. " +
                    "httpProtocol={protocol} isWebSocketRequest={isWs} nonBearerProtocols=[{protocols}]",
                    BearerProtocolPrefix,
                    HttpContext.Request.Protocol,
                    HttpContext.WebSockets.IsWebSocketRequest,
                    string.Join(", ", HttpContext.WebSockets.WebSocketRequestedProtocols
                        .Where(p => !p.StartsWith(BearerProtocolPrefix, StringComparison.Ordinal))));
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Sec-WebSocket-Protocol values must be RFC 7230 tokens, so the token is sent
            // base64url-encoded (no '=' padding, '-'/'_' instead of '+'/'/'). Restore standard
            // base64 before parsing.
            var token64 = Base64UrlToBase64(bearerProtocol.Substring(BearerProtocolPrefix.Length));

            IOdinContext? authenticatedContext;
            try
            {
                if (!ClientAuthenticationToken.TryParse(token64, out var clientAuthToken))
                {
                    throw new OdinSecurityException("Malformed token");
                }

                authenticatedContext = await ResolveContextAsync(clientAuthToken);
            }
            catch (OdinSecurityException e)
            {
                _logger.LogWarning("V2 WS upgrade rejected (401): token parsed but security check failed: {error} " +
                                   "httpProtocol={protocol}", e.Message, HttpContext.Request.Protocol);
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (authenticatedContext == null)
            {
                _logger.LogWarning("V2 WS upgrade rejected (401): app permission context resolved to null for a " +
                                   "parsed token. httpProtocol={protocol}", HttpContext.Request.Protocol);
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await AcceptAndRunAsync(authenticatedContext, NegotiatedSubProtocol);
        }

        private async Task AcceptAndRunAsync(IOdinContext authenticatedContext, string? subProtocol)
        {
            using var cancellationTokenSources = CancellationTokenSource.CreateLinkedTokenSource(
                HttpContext.RequestAborted,
                _hostApplicationLifetime.ApplicationStopping);

            try
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    DangerousEnableCompression = true,
                    SubProtocol = subProtocol,
                });

                await _notificationHandler.EstablishConnection(webSocket, authenticatedContext, cancellationTokenSources.Token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (InvalidOperationException)
            {
                // this can happen when we need to upgrade
                //System.InvalidOperationException: The response status code for a Extended CONNECT request must be 2XX.
                VersionUpgradeScheduler.SetRequiresUpgradeResponse(HttpContext);
            }
        }

        private async Task<IOdinContext?> ResolveContextAsync(ClientAuthenticationToken clientAuthToken)
        {
            IOdinContext? ctx;
            string authContextName;

            switch (clientAuthToken.ClientTokenType)
            {
                case ClientTokenType.App:
                    ctx = await _appRegistrationService.GetAppPermissionContextAsync(clientAuthToken, WebOdinContext);
                    authContextName = "websocket-app-token";
                    break;

                default:
                    throw new OdinSecurityException("Invalid Client Token Type");
            }

            // GetAppPermissionContextAsync returns a token-keyed CACHED IOdinContext that is
            // shared across requests. Calling SetAuthContext on it mutates that shared instance:
            // the first WS upgrade for a token sets AuthContext, and every subsequent upgrade for
            // the same token then throws OdinSecurityException("Cannot reset auth context") -> 401
            // until the cache entry expires. That is the intermittent reconnect-401 flap. Clone
            // first so the per-connection auth context never touches the cached entry.
            var resolved = ctx?.Clone();
            resolved?.SetAuthContext(authContextName);
            return resolved;
        }

        internal static string Base64UrlToBase64(string base64Url)
        {
            var s = base64Url.Replace('-', '+').Replace('_', '/');
            var padLen = (4 - s.Length % 4) % 4;
            return padLen == 0 ? s : s + new string('=', padLen);
        }
    }
}
