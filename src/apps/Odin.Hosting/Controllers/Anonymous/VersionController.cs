using Microsoft.AspNetCore.Mvc;

namespace Odin.Hosting.Controllers.Anonymous;

public class VersionController
{
    [HttpGet("/api/v1/version")]
    public ActionResult<string> GetVersion(string filename)
    {
        return Version.VersionText;
    }
}