using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Routing;
using NUnit.Framework;
using Odin.Hosting.UnifiedV2;

namespace Odin.Hosting.Tests._V2.Tests.Routing;

/// <summary>
/// Two controllers mounted on the same prefix that each declare the same action route make every
/// request to it throw AmbiguousMatchException -> 500, before authentication ever runs, and nothing
/// in the build says so. This walks the UnifiedV2 controllers the way MVC does and fails instead.
/// </summary>
[TestFixture]
public class V2RouteUniquenessTests
{
    [Test]
    public void EveryV2EndpointHasAUniqueMethodAndTemplate()
    {
        var routes = new List<(string Verb, string Template, string Source)>();

        var controllers = typeof(UnifiedApiRouteConstants).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace?.StartsWith("Odin.Hosting.UnifiedV2") == true);

        foreach (var controller in controllers)
        {
            var prefixes = Templates(controller.GetCustomAttributes(inherit: true));
            if (prefixes.Count == 0)
            {
                prefixes = [""];
            }

            // Inherited actions matter: the V2 controllers are empty shells over shared base controllers.
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object)))
            {
                var attributes = method.GetCustomAttributes(inherit: true);
                var verbs = attributes.OfType<IActionHttpMethodProvider>().SelectMany(a => a.HttpMethods).Distinct().ToList();
                if (verbs.Count == 0)
                {
                    continue;
                }

                var suffixes = Templates(attributes);
                if (suffixes.Count == 0)
                {
                    suffixes = [""];
                }

                foreach (var verb in verbs)
                foreach (var prefix in prefixes)
                foreach (var suffix in suffixes)
                {
                    routes.Add((verb, Combine(prefix, suffix), $"{controller.Name}.{method.Name}"));
                }
            }
        }

        Assert.That(routes, Is.Not.Empty, "No UnifiedV2 endpoints were discovered - the walk is broken, not the routes.");

        var duplicates = routes
            .GroupBy(r => (r.Verb, r.Template))
            .Where(g => g.Select(r => r.Source).Distinct().Count() > 1)
            .Select(g => $"{g.Key.Verb} {g.Key.Template} <- {string.Join(", ", g.Select(r => r.Source).Distinct())}")
            .ToList();

        Assert.That(duplicates, Is.Empty, "Ambiguous V2 routes:\n" + string.Join("\n", duplicates));
    }

    private static List<string> Templates(IEnumerable<object> attributes) =>
        attributes.OfType<IRouteTemplateProvider>()
            .Select(a => a.Template)
            .Where(t => t != null)
            .Distinct()
            .ToList();

    private static string Combine(string prefix, string suffix)
    {
        if (suffix.StartsWith('/') || suffix.StartsWith("~/", StringComparison.Ordinal))
        {
            return suffix.TrimStart('~');
        }

        return string.IsNullOrEmpty(suffix) ? prefix : $"{prefix.TrimEnd('/')}/{suffix}";
    }
}
