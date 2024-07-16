using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Odin.Hosting.Controllers.APIv2.Base;

public class ApiV2RouteConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        var odinRoute = action.Attributes.OfType<OdinRouteAttribute>().FirstOrDefault();
        if (odinRoute != null)
        {
            var templates = GetTemplates(odinRoute.Flags);
            foreach (var template in templates)
            {
                action.Selectors.Add(new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel { Template = $"{template}{odinRoute.Suffix}" }
                });
            }
        }
    }

    private string[] GetTemplates(RootApiRoutes flags)
    {
        var templates = new List<string>();

        if (flags.HasFlag(RootApiRoutes.Owner))
        {
            templates.Add("/api/owner/v2");
        }
        
        if (flags.HasFlag(RootApiRoutes.Apps))
        {
            templates.Add("/api/apps/v2");
        }
        
        if (flags.HasFlag(RootApiRoutes.Guest))
        {
            templates.Add("/api/guest/v2");
        }

        return templates.ToArray();
    }
}
