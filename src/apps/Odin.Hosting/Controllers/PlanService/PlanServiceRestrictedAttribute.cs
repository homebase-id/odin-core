using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Odin.Hosting.Controllers.PlanService;

/// <summary>
/// The plan service API answers only on the provisioning domain, and only when this host has a plan service key.
/// Controllers are mapped on every host name, so the host is checked here, not just in Startup's branch. Anything
/// else is a 404, to not confirm the endpoint exists; a wrong or missing key is a 401.
/// </summary>
public class PlanServiceRestrictedAttribute(string apiKey, string provisioningDomain) : ActionFilterAttribute
{
    private readonly byte[] _apiKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (apiKey == "" || context.HttpContext.Request.Host.Host != provisioningDomain)
        {
            // Strange hack to circumvent creation of a ProblemDetails
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.NotFound };
            return;
        }

        // Compared as hashes, in constant time, so neither the key nor its length leaks through timing
        var sent = context.HttpContext.Request.Headers[PlanServiceApiPathConstants.ApiKeyHeader].FirstOrDefault() ?? "";
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(sent)), _apiKeyHash))
        {
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.Unauthorized };
            return;
        }

        base.OnActionExecuting(context);
    }
}
