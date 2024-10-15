using System;

namespace Odin.Hosting.Controllers.APIv2.Base;

[Flags]
public enum RootApiRoutes
{
    Owner = 2,
    Apps = 4,
    Guest = 8
}