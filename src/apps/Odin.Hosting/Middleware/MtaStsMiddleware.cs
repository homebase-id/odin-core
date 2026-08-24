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

    public async Task Invoke(HttpContext context)
    {
        var tenantMail = config.Email.TenantMail;
        if (!tenantMail.Enabled || !context.Request.Host.Host.StartsWith(HostPrefix))
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
