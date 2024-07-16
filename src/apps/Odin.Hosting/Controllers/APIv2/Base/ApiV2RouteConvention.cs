using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;
using Odin.Core.Exceptions;

namespace Odin.Hosting.Controllers.APIv2.Base;

public class ApiV2RouteConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var odinRoute = controller.Attributes.OfType<OdinRouteAttribute>().FirstOrDefault();
            if (odinRoute != null)
            {
                var templates = GetTemplates(odinRoute.Flags);
                foreach (var action in controller.Actions)
                {
                    var s = action.Selectors.SingleOrDefault();
                    if (null == s?.AttributeRouteModel)
                    {
                        throw new OdinSystemException("There must be at least one http method attribute in a route");
                    }
                    
                    var httpMethodAttributes = action.Attributes.OfType<IActionHttpMethodProvider>().ToList();
                    if (!httpMethodAttributes.Any())
                    {
                        throw new OdinSystemException("There must be at least one http method attribute in a route");
                    }

                    var actionTemplate = s.AttributeRouteModel.Template;
                    action.Selectors.Clear();

                    foreach (var template in templates)
                    {
                        var finalTemplate = $"{template}{odinRoute.Prefix}/{actionTemplate}";

                        foreach (var httpMethodAttribute in httpMethodAttributes)
                        {
                            var selector = new SelectorModel
                            {
                                AttributeRouteModel = new AttributeRouteModel { Template = finalTemplate }
                            };
                            selector.ActionConstraints.Add(new HttpMethodActionConstraint(httpMethodAttribute.HttpMethods));
                            action.Selectors.Add(selector);
                        }
                    }
                }
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