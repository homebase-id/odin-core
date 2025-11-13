using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.UnifiedV2.Authentication;

namespace Odin.Hosting.UnifiedV2;

public class UnifiedV2AuthorizeAttribute : AuthorizeAttribute
{
    public UnifiedV2AuthorizeAttribute()
    {
        AuthenticationSchemes = UnifiedAuthConstants.SchemeName;
    }
}

public static class UnifiedApiRouteConstants
{
    public const string BasePath = "/api/v2";
    public const string Drive = BasePath + "/drive";
    public const string Files = Drive + "/files";
}
