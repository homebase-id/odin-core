using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Extensions;

namespace Odin.Hosting.Controllers.Anonymous;

public class VersionController
{
    [HttpGet("/api/v1/version")]
    public ActionResult<string> GetVersion()
    {
        return Version.VersionText;
    }
}
