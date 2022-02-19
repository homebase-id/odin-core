using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Authorization;

namespace Youverse.Hosting.Authentication.YouAuth
{
    public class YouAuthAuthenticationHandler : AuthenticationHandler<YouAuthAuthenticationSchemeOptions>
    {
        private readonly IYouAuthSessionManager _youAuthSessionManager;

        public YouAuthAuthenticationHandler(
            IOptionsMonitor<YouAuthAuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IYouAuthSessionManager youAuthSessionManager)
            : base(options, logger, encoder, clock)
        {
            _youAuthSessionManager = youAuthSessionManager;
        }

        //

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!TryGetSessionIdFromCookie(out Guid sessionId, out byte[] xTokenHalfKey))
            {
                return AuthenticateResult.Fail("No sessionId cookie");
            }

            var session = await _youAuthSessionManager.LoadFromId(sessionId);
            if (session == null)
            {
                return AuthenticateResult.Fail("No session matching session id");
            }
            
            var claims = new[]
            {
                new Claim(YouAuthDefaults.IdentityClaim, session.Subject),
                new Claim(DotYouClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer),
                new Claim(DotYouClaimTypes.IsIdentified, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, DotYouClaimTypes.YouFoundationIssuer)
            };
            
            //TODO: 
            if (xTokenHalfKey?.Length > 0)
            {
                //use the session xtoken to load more access
                /*
                 * essentially the xtoken offers access to more drives by giving their keys.  so essentially it is like an app
                 * this means they need access to a drive if they have a key for it.
                 *
                 * now - currently drive permissions are on the app context and xtoken is just like an app... so i wonder if there's another name for this..
                 * maybe we just call it DriveContext and that's where we check drive permission?
                 *
                 * but of course we have to figure out if users are specifying a drive or an app when querying using the xotekn?
                 *
                 * currently you have to use an appid only .. and from there we look up the default drive.
                 * that also denotes the permission to other drives that app has access to.
                 *
                 * so there could be a drivecontext which denotes drive permissions.. but what about other permissions?
                 *
                 * can manage connections?
                 *  I think XToken is all about data.  there ARE scenarios where where it needs access to view connections... i think?
                 *
                 * so let us split permissions -
                 *
                 * - there is a operations permissions
                 * - there are drive permissions
                 *
                 * - owner comes in the owner console and an app
                 *
                 * - transit brings an app id and also needs permissions to read a drive, which can come with the xtoken
                 *  - let us take a scenario where I need to get profile data via transit -
                 *  - when a requests comes for profile data -
                 *      - i can present an xtoken which is used to get drive access
                 *      - if the requester is connected, we can use that token
                 *      - if the requester presents an xtoken and is connected;
                 *          - we use the token from the connection.
                 *  - when receiving data via transit an app is specified - the issue here is the appid alone is not a means of authenticating access to a drive
                 *  -
                 *
                 * so breaking it down -
                 * DriveContext - grants permissions to a given set a drives; needs
                 *  - GetStorageKey(driveId)
                 *  - CanWriteDrive(driveId)
                 *  - CanReadDrive(driveId)
                 *
                 * 
                 * AppContext
                 *  - CanWriteConnections()
                 *  - CanReadConnections()
                 *  - CanReadCircles()
                 *  - TODO: Other Permissions
                 *
                 *
                 * unless you make an Xtoken app
                 *  - the only outlier here is transit.  it can come in the following scnearios
                 *      - incoming file transfer (that needs an appid as the target but does it have an xtoken?)
                 *  it uses the xtoken of connection.
                 * in the case of best buy- it uses the xtoken that grants access.. that xtoken is used
                 *
                 * app to app transfer
                 * di retrieving data (but isnt this an app?)
                 */
            }

            var claimsIdentity = new ClaimsIdentity(claims, nameof(YouAuthAuthenticationHandler));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), this.Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }

        //

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Not authenticated",
                Instance = Context.Request.Path
            };
            var json = JsonSerializer.Serialize(problemDetails);

            Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            Context.Response.ContentType = "application/problem+json";
            return Context.Response.WriteAsync(json);
        }

        //

        private bool TryGetSessionIdFromCookie(out Guid sessionId, out byte[] xTokenHalfKey)
        {
            var value = Context.Request.Cookies[YouAuthDefaults.SessionCookieName] ?? "";
            xTokenHalfKey = default;
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (Guid.TryParse(value, out sessionId))
                {
                    var xtokenValue = Context.Request.Cookies[YouAuthDefaults.XTokenCookieName];
                    if (!string.IsNullOrWhiteSpace(xtokenValue))
                    {
                        xTokenHalfKey = Convert.FromBase64String(xtokenValue);
                    }

                    return true;
                }
            }

            sessionId = default;
            return false;
        }
    }
}