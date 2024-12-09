using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Fingering;

namespace Odin.Hosting.Controllers.Anonymous;

// Make routes in here are:
// - are accessible without authentication
// - are accessible using http

[ApiController]
[Route(".well-known/did.json")]
public class DidController(IDidService iDidService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var response = await iDidService.GetDidWebAsync();
        return response == null ? NotFound() : Ok(response);
    }
}