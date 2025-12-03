using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.UnifiedV2.Authentication;

namespace Odin.Hosting.UnifiedV2;

public class UnifiedV2AuthorizeAttribute : AuthorizeAttribute
{
    public UnifiedV2AuthorizeAttribute(string policy = null)
    {
        AuthenticationSchemes = UnifiedAuthConstants.SchemeName;
        Policy = policy;
    }
}