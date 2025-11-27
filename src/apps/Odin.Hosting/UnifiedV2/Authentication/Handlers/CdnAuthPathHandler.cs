#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public static class CdnAuthPathHandler
{
    static readonly List<string> AllowedPaths =  
    [
        $"{UnifiedApiRouteConstants.Files}/payload",
        $"{UnifiedApiRouteConstants.Files}/thumb"
    ];

    public static async Task<AuthenticateResult> Handle(HttpContext context, IOdinContext odinContext)
    {
        if (!IsValidPath(context))
        {
            return AuthenticateResult.Fail("Invalid path");
        }
        
        // do the rest - build the odincontext etc.

        await Task.CompletedTask;
        throw new NotImplementedException("TODO");
    }

    private static bool IsValidPath(HttpContext context)
    {
        var path = context.Request.Path;
    
        foreach (var allowed in AllowedPaths)
        {
            if (path.StartsWithSegments(allowed) ||
                path.StartsWithSegments(allowed + ".")) // the . is to handle thumbnail.{extension}
            {
                return true;
            }
        }

        return false;
    }

}