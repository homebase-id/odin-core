using System.Collections.Generic;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Utils;

public class AppTransitTestUtilsContext : TransitTestUtilsContext
{
    public TestAppContext TestAppContext { get; set; }
}