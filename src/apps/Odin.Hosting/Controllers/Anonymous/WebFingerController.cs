using Microsoft.AspNetCore.Mvc;
using Odin.Services.Certificate;

namespace Odin.Hosting.Controllers.Anonymous;

// Make routes in here are:
// - are accessible without authentication
// - are accessible using http

[ApiController]
[Route(".well-known/webfinger")]
public class WebFingerController(IAcmeHttp01TokenCache cache) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok("hola");
    }
}
