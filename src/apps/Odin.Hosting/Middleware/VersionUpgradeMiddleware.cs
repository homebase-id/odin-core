using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Configuration;
using Odin.Hosting.Controllers.OwnerToken.DataConversion;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
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

                // The version-info endpoint is how a client finds out what the upgrade is doing, and
                // blocking it means the one question worth asking during an upgrade is the one that
                // cannot be asked. It reads two config values and writes nothing. The rest of the
                // data-conversion controller mutates, so it stays behind the guard.
                if (!path.Contains(OwnerDataConversionController.VersionInfoEndpoint))
                {
                    // Authorizing an app or a YouAuth sign-in walks the owner's own browser through
                    // /api/owner/v1/youauth/authorize -- first a navigation, then the consent form's
                    // POST. The refusal below has no body, so mid-upgrade the owner was left looking
                    // at a bare 503 from their browser. The consent screen's own upgrade check cannot
                    // cover it: reaching that screen depends on these very requests.
                    //
                    // Send the browser to the owner console's upgrade screen instead. It polls the
                    // version-info endpoint exempted above and returns to returnUrl once the upgrade
                    // is done, so the sign-in the owner started carries on rather than dying.
                    //
                    // Only for browser navigations by the owner. Everything else keeps the 503 and the
                    // header above, which is what callers like CircleNetworkIntroductionService and
                    // the YouAuth token exchange read to tell an upgrading identity from a broken one.
                    if (IsOwnerConsoleNavigation(context, path))
                    {
                        return RedirectToUpgradeScreenAsync(context);
                    }

                    context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                    return Task.CompletedTask;
                }
            }

            return next(context);
        }

        /// <summary>
        /// Sends the browser to the owner console's upgrade screen, telling it where to resume. Falls
        /// back to the refusal if where-to-resume cannot be established.
        /// </summary>
        private static async Task RedirectToUpgradeScreenAsync(HttpContext context)
        {
            var resumeUrl = await ResolveResumeUrlAsync(context);
            if (resumeUrl == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                return;
            }

            var target = $"{OwnerFrontendPathConstants.DataUpgrade}?returnUrl={WebUtility.UrlEncode(resumeUrl)}";

            // See Other for the consent POST: the upgrade screen is a page to be fetched, not the
            // form's handler re-run, and 303 is what says so without relying on the convention that
            // browsers downgrade a 302'd POST to a GET.
            context.Response.StatusCode = (int)(HttpMethods.IsPost(context.Request.Method)
                ? HttpStatusCode.SeeOther
                : HttpStatusCode.Redirect);
            context.Response.Headers.Location = target;
        }

        /// <summary>
        /// Where to send the browser once the upgrade finishes: the request itself for a navigation,
        /// or the URL the consent form was posted on behalf of.
        /// </summary>
        private static async Task<string?> ResolveResumeUrlAsync(HttpContext context)
        {
            var request = context.Request;

            if (!HttpMethods.IsPost(request.Method))
            {
                return request.GetDisplayUrl();
            }

            // A form POST carries its parameters in the body, so the request URL alone is not enough
            // to come back to -- resuming there would fail validation for want of a redirect_uri. The
            // consent form posts the authorize URL it was rendered for, which is also where the
            // controller redirects on success, so resuming there re-renders consent and the owner
            // finishes the sign-in with one more click.
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

            // Same checks the consent handler makes on this value (Sanity #1 and #2). Here they also
            // keep the upgrade screen from being turned into an open redirect by a forged form.
            if (returnUri.Host != request.Host.Host || returnUri.AbsolutePath != request.Path)
            {
                return null;
            }

            return returnUrl;
        }

        /// <summary>
        /// True when this request is a browser being navigated to an owner endpoint while carrying an
        /// owner session -- the only case where the owner console's upgrade screen is both reachable
        /// and able to say anything useful.
        /// </summary>
        private static bool IsOwnerConsoleNavigation(HttpContext context, string path)
        {
            var request = context.Request;

            // Owner paths only. A guest or public-app navigation has no business being sent into the
            // owner console of the identity it is visiting.
            if (!path.StartsWith(OwnerApiPathConstants.BasePathV1))
            {
                return false;
            }

            // GET/HEAD for a plain navigation; POST for a form submitted by one, which is how consent
            // is given. Other verbs are APIs being called, not pages being visited.
            if (!HttpMethods.IsGet(request.Method) &&
                !HttpMethods.IsHead(request.Method) &&
                !HttpMethods.IsPost(request.Method))
            {
                return false;
            }

            // The upgrade screen is owner-authenticated and polls owner endpoints, so a browser
            // without the cookie would only get an error page out of it -- worse than the 503 it
            // replaces. This middleware runs ahead of authentication, so whether the cookie is still
            // valid is not knowable here; that is the screen's problem, as it is for any owner page.
            if (string.IsNullOrEmpty(request.Cookies[OwnerAuthConstants.CookieName]))
            {
                return false;
            }

            // Sent by current browsers on every top-level navigation and by nothing else, so where it
            // is present it settles the question on its own. Checked before Accept so that a fetch()
            // which happens to ask for HTML is not mistaken for a navigation.
            var fetchDest = request.Headers["Sec-Fetch-Dest"].ToString();
            if (!string.IsNullOrEmpty(fetchDest))
            {
                return fetchDest == "document";
            }

            return request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
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
