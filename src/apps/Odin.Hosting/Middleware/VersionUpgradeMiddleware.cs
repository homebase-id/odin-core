#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Configuration;
using Odin.Hosting.Controllers.OwnerToken.DataConversion;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;

namespace Odin.Hosting.Middleware
{
    public class VersionUpgradeMiddleware(RequestDelegate next)
    {
        // Note: the run state is resolved from the request scope rather than injected as a method
        // parameter. Method-injected parameters are resolved on every request, before the path checks
        // below get a chance to short-circuit. (VersionUpgradeScheduler was injected but never used.)
        public Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value;
            
            if (path == null)
            {
                return next(context);
            }

            if (!path.StartsWith("/api"))
            {
                return next(context);
            }

            if (path.Contains(OwnerConfigurationController.InitialSetupEndpoint))
            {
                return next(context);
            }

            if (path.StartsWith(OwnerApiPathConstants.AuthV1))
            {
                return next(context);
            }

            var runState = context.RequestServices.GetRequiredService<VersionUpgradeRunState>();
            if (runState.IsRunning)
            {
                context.Response.Headers.Append(OdinHeaderNames.UpgradeIsRunning, bool.TrueString);

                if (!AnswerableDuringUpgrade(context.Request, path))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;

                    // The refusal is what every caller gets -- and for machine callers it is what they
                    // want: CircleNetworkIntroductionService and the YouAuth token exchange both read
                    // the header above off it to tell an upgrading identity from a broken one.
                    //
                    // Except on the one endpoint a browser is navigated to rather than called.
                    if (IsYouAuthAuthorizeByOwner(context.Request))
                    {
                        return RedirectToUpgradeScreenAsync(context);
                    }

                    return Task.CompletedTask;
                }
            }

            return next(context);
        }

        /// <summary>
        /// The questions that must stay answerable while everything else is refused.
        /// </summary>
        /// <remarks>
        /// All of these are reads that touch no tenant data, and refusing any of them turns "this
        /// identity is busy" into something worse than a wait:
        /// <list type="bullet">
        /// <item><c>data-version-info</c> is how a client finds out what the upgrade is doing -- the
        /// one question worth asking during an upgrade. It reads two config values and writes
        /// nothing; the rest of that controller mutates and stays behind the guard.</item>
        /// <item><c>auth/ident</c> is how anything finds out this host is an identity at all, and it
        /// is the first call every login box makes. Clients read <c>odinId</c> out of the body and
        /// treat a failed parse as "no such identity", so a bodiless 503 does not make an upgrading
        /// identity look busy -- it makes it look like it does not exist.</item>
        /// <item><c>health/*</c> is the liveness answer: ping names the host, ip names the caller.
        /// A health check that fails while the service is deliberately busy is a health check that
        /// reports an outage that is not happening.</item>
        /// </list>
        /// </remarks>
        private static bool AnswerableDuringUpgrade(HttpRequest request, string path)
        {
            return path.Contains(OwnerDataConversionController.VersionInfoEndpoint) ||
                   request.Path.StartsWithSegments(GuestApiPathConstantsV1.IdentV1) ||
                   request.Path.StartsWithSegments(UnifiedApiRouteConstants.Health);
        }

        /// <summary>
        /// The YouAuth authorize endpoint, reached by the owner's own browser.
        /// </summary>
        /// <remarks>
        /// Authorizing an app or a YouAuth sign-in navigates the browser here and then posts the
        /// consent form back to the same place, so a bodiless 503 is rendered by the browser as a
        /// bare error with nothing to act on. It is the same endpoint
        /// <c>OwnerAuthenticationHandler</c> singles out in its own <c>RedirectPaths</c>, for
        /// the same reason: this is where a refusal has to reach a person rather than a program.
        /// Widen this to a shared list rather than a second one if a second endpoint ever needs it.
        /// </remarks>
        private static bool IsYouAuthAuthorizeByOwner(HttpRequest request)
        {
            if (!request.Path.StartsWithSegments(OwnerApiPathConstants.YouAuthV1Authorize))
            {
                return false;
            }

            // The endpoint is [AuthorizeValidOwnerToken] and UseAuthorization runs ahead of this
            // middleware (Startup), so a browser without a session is already redirected to login and
            // never arrives here. Checked anyway because the cost of being wrong is not a 503: the
            // upgrade screen is owner-only, and it returns to where it came from on its own, so a
            // session-less browser sent there would bounce between the two.
            return !string.IsNullOrEmpty(request.Cookies[OwnerAuthConstants.CookieName]);
        }

        /// <summary>
        /// Replaces the refusal with a redirect to the owner console's upgrade screen, telling it
        /// where to resume. Leaves the refusal in place if where-to-resume cannot be established.
        /// </summary>
        /// <remarks>
        /// That screen polls the version-info endpoint exempted above and returns to <c>returnUrl</c>
        /// once the upgrade is done, so the sign-in the owner started carries on instead of dying.
        /// </remarks>
        private static async Task RedirectToUpgradeScreenAsync(HttpContext context)
        {
            var request = context.Request;

            string? resumeUrl;
            HttpStatusCode status;

            if (HttpMethods.IsPost(request.Method))
            {
                resumeUrl = await ReadConsentReturnUrlAsync(request);

                // See Other, not Found: the upgrade screen is a page to be fetched, not the consent
                // form's handler re-run, and 303 says so without relying on the convention that
                // browsers downgrade a 302'd POST to a GET.
                status = HttpStatusCode.SeeOther;
            }
            else
            {
                resumeUrl = request.GetDisplayUrl();
                status = HttpStatusCode.Redirect;
            }

            if (resumeUrl == null)
            {
                return;
            }

            context.Response.StatusCode = (int)status;
            context.Response.Headers.Location =
                $"{OwnerFrontendPathConstants.DataUpgrade}?returnUrl={WebUtility.UrlEncode(resumeUrl)}";
        }

        /// <summary>
        /// The authorize URL the consent form was rendered for, or null if this is not that form.
        /// </summary>
        /// <remarks>
        /// A form POST carries its parameters in the body, so the request URL alone is not somewhere
        /// to come back to -- resuming there would fail validation for want of a redirect_uri. The
        /// consent form posts the authorize URL it belongs to, which is also where the controller
        /// redirects on success, so resuming there re-renders consent and the owner finishes the
        /// sign-in with one more click.
        /// </remarks>
        private static async Task<string?> ReadConsentReturnUrlAsync(HttpRequest request)
        {
            if (!request.HasFormContentType)
            {
                return null;
            }

            var form = await request.ReadFormAsync();
            var returnUrl = form[YouAuthAuthorizeConsentGiven.ReturnUrlName].ToString();

            if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var returnUri))
            {
                return null;
            }

            // The same checks the consent handler makes on this value (its Sanity #1 and #2). Here
            // they also keep the upgrade screen from being turned into an open redirect by a forged
            // form. Keep the two in step: this one refuses by leaving the 503, that one by 400.
            if (returnUri.Host != request.Host.Host || returnUri.AbsolutePath != request.Path)
            {
                return null;
            }

            return returnUrl;
        }
    }

    public static class VersionUpgradeMiddlewareExtensions
    {
        public static IApplicationBuilder UseVersionUpgrade(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<VersionUpgradeMiddleware>();
        }
    }
}
