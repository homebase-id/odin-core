#nullable enable
using System;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication
{
    public class UnifiedAuthenticationHandler : AuthenticationHandler<UnifiedAuthenticationSchemeOptions>, IAuthenticationSignInHandler
    {
        /// <summary/>
        public UnifiedAuthenticationHandler(IOptionsMonitor<UnifiedAuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <summary/>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // SEB:TODO we should split up these two into different handlers
            if (Request.Path.StartsWithSegments(OwnerApiPathConstants.YouAuthV1Authorize))
            {
                var returnUrl = WebUtility.UrlEncode(Request.GetDisplayUrl());
                var loginUrl = $"{Request.Scheme}://{Request.Host}{OwnerFrontendPathConstants.Login}?returnUrl={returnUrl}";
                Response.Redirect(loginUrl);
            }
            else
            {
                Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            }

            return Task.CompletedTask;
        }

        /// <summary/>
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // look for the cat
            var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();

            if (!TryFindThePussyCat(out var token))
            {
                return AuthenticateResult.Fail("Invalid Token");
            }

            switch (token.ClientTokenType)
            {
                case ClientTokenType.BuiltInBrowserApp:
                case ClientTokenType.YouAuth:
                    return await GuestAuthPathHandler.Handle(Context, odinContext);

                case ClientTokenType.Owner:
                    return await OwnerAuthPathHandler.Handle(Context, odinContext);

                case ClientTokenType.App:
                    return await AppAuthPathHandler.Handle(Context, odinContext);

                default:
                    return AuthenticateResult.Fail("Invalid Path");
            }
        }

        private bool TryFindThePussyCat(out ClientAuthenticationToken clientAuthToken)
        {
            if (AuthUtils.TryGetClientAuthToken(this.Context, YouAuthConstants.AppCookieName, out clientAuthToken, true))
            {
                return true;
            }

            if (AuthUtils.TryGetClientAuthToken(this.Context, YouAuthDefaults.XTokenCookieName, out clientAuthToken))
            {
                return true;
            }

            return AuthUtils.TryGetClientAuthToken(this.Context, OwnerAuthConstants.CookieName, out clientAuthToken);
        }

        public async Task SignOutAsync(AuthenticationProperties? properties)
        {
            if (properties == null)
            {
                return;
            }

            if (properties.Items.TryGetValue(nameof(ClientTokenType), out var value))
            {
                if (Enum.TryParse<ClientTokenType>(value, out var tt))
                {
                    var odinContext = Context.RequestServices.GetRequiredService<IOdinContext>();

                    switch (tt)
                    {
                        case ClientTokenType.BuiltInBrowserApp:
                        case ClientTokenType.YouAuth:
                            await GuestAuthPathHandler.HandleSignOut(Context, odinContext);
                            break;
                        case ClientTokenType.Owner:
                            await OwnerAuthPathHandler.HandleSignOut(Context);
                            break;

                        case ClientTokenType.App:
                            await AppAuthPathHandler.HandleSignOut(Context, odinContext);
                            break;
                    }
                }
            }
        }

        public Task SignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }
    }
}