#nullable enable
using Microsoft.AspNetCore.Authorization;
using Odin.Core.Exceptions;
using Odin.Hosting.UnifiedV2.Authentication;

namespace Odin.Hosting.UnifiedV2;

public class UnifiedV2AuthorizeAttribute : AuthorizeAttribute
{
    public UnifiedV2AuthorizeAttribute(string policyName)
    {
        if (string.IsNullOrEmpty(policyName) || string.IsNullOrWhiteSpace(policyName))
        {
            throw new OdinSystemException("policy name required; use UnifiedPolicies.Anonymous at a minimum");
        }
        
        AuthenticationSchemes = UnifiedAuthConstants.SchemeName;
        Policy = policyName;
    }
}