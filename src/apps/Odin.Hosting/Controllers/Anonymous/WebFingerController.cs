using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Fingering;

namespace Odin.Hosting.Controllers.Anonymous;

// Make routes in here are:
// - are accessible without authentication
// - are accessible using http

[ApiController]
[Route(".well-known/webfinger")]
public class WebFingerController(IWebfingerService webfingerService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var response = await webfingerService.GetWebFingerAsync();
        return response == null ? NotFound() : Ok(response);
    }
}


