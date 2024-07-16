using System;

namespace Odin.Hosting.Controllers.APIv2.Base;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class OdinRouteAttribute(RootApiRoutes flags, string suffix) : Attribute
{
    public RootApiRoutes Flags { get; } = flags;

    public string Suffix { get; } = suffix;
}