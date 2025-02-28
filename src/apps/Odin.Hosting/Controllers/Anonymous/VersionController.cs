using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services;

namespace Odin.Hosting.Controllers.Anonymous;

public class VersionController
{
    [HttpGet("/api/v1/version")]
    public ActionResult<string> GetVersion()
    {
        return Version.VersionText;
    }

    [HttpGet("/api/v1/twiddle/{seconds}")]
    public async Task<ActionResult<string>> Twiddle(int seconds)
    {
        await Task.Delay(seconds * 1000);
        return $"Twiddled my fingers for {seconds} seconds";
    }
}
