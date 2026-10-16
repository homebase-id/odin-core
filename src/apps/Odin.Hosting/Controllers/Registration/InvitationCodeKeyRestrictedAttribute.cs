using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Odin.Services.Registry.Registration;

namespace Odin.Hosting.Controllers.Registration;

/// <summary>
/// Creating and listing invitation codes needs the key (#1908). PROTOTYPE: the key is hard-coded in
/// <see cref="InvitationCodePrototype"/>. 404 when this host has invitation codes off, 401 without the key.
/// </summary>
public class InvitationCodeKeyRestrictedAttribute(InvitationCodeStore invitationCodes) : ActionFilterAttribute
{
    private static readonly byte[] ExpectedKey = Encoding.UTF8.GetBytes(InvitationCodePrototype.ApiKey);

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!invitationCodes.IsEnabled)
        {
            // Strange hack to circumvent creation of a ProblemDetails
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.NotFound };
            return;
        }

        var presented = context.HttpContext.Request.Headers[InvitationCodePrototype.ApiKeyHeader].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), ExpectedKey))
        {
            context.Result = new ObjectResult(null) { StatusCode = (int)HttpStatusCode.Unauthorized };
            return;
        }

        base.OnActionExecuting(context);
    }
}
