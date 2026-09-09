using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Services.Configuration;
using Odin.Services.Email;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Middleware;

#nullable enable

/// <summary>
/// Serves the MTA-STS policy at https://mta-sts.&lt;domain&gt;/.well-known/mta-sts.txt
/// (docs/email-dns-plan.md). Runs BEFORE RedirectIfNotApexMiddleware because the spec
/// (RFC 8461) forbids redirects on the policy fetch, and before tenant resolution because
/// the policy body is pure config - identical for every tenant of this host group.
/// </summary>
public sealed class MtaStsMiddleware(RequestDelegate next, OdinConfiguration config)
{
    private const string HostPrefix = DnsConfigurationSet.PrefixMtaSts + ".";
    private static readonly PathString AcmeChallengePath = new("/.well-known/acme-challenge");

    public async Task Invoke(HttpContext context)
    {
        var tenantMail = config.Email.TenantMail;
        if (!tenantMail.Enabled || !context.Request.Host.Host.StartsWith(HostPrefix))
        {
            await next(context);
            return;
        }

        //
        // The HTTP-01 challenge for THIS host must reach CertesAcmeMiddleware, which runs after
        // us. Without this, every path but the policy 404s - including the challenge - so the
        // mta-sts SAN could never validate on any host with tenant mail enabled, which is the
        // only configuration in which it is requested. That deterministic 404 is what Let's
        // Encrypt was counting against the failed-authorization allowance on 2026-09-08.
        // RedirectIfNotApexMiddleware avoids the same trap by passing plain HTTP through.
        //
        if (context.Request.Path.StartsWithSegments(AcmeChallengePath))
        {
            await next(context);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/.well-known/mta-sts.txt")
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(MtaStsPolicy.Build(tenantMail.MxNodes));
            return;
        }

        // The mta-sts host serves the policy and nothing else
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync("Not found");
    }
}
