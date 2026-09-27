using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Fingering;

namespace Odin.Hosting.Controllers.Anonymous;

// Make routes in here are:
// - are accessible without authentication
// - are accessible using http

[ApiController]
[Route(YouAuthDefaults.ClientMetadataPath)]
public class YouAuthClientMetadataController(IOwnClientMetadataService ownClientMetadataService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await ownClientMetadataService.GetAsync());
    }
}
