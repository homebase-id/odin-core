using System.Collections.Generic;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Hosting.Tests.AppAPI.Utils;

public class AppTransitTestUtilsContext : TransitTestUtilsContext
{
    public TestSampleAppContext TestAppContext { get; set; }
}