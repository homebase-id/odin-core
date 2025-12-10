using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public interface IAuthPathHandler
{
    Task<AuthHandlerResult> HandleAsync(
        HttpContext context,
        ClientAuthenticationToken token,
        IOdinContext odinContext);

    Task HandleSignOutAsync(
        Guid tokenId,
        HttpContext context,
        IOdinContext odinContext);
}