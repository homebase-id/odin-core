using Microsoft.AspNetCore.Mvc;
using Odin.Services.Certificate;

namespace Odin.Hosting.Controllers.Anonymous;

// Make routes in here are:
// - are accessible without authentication
// - are accessible using http

[ApiController]
[Route(".well-known/acme-challenge")]
public class AcmeChallengeController(IAcmeHttp01TokenCache cache) : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok("pong");
    }

    [HttpGet("{token}")]
    public IActionResult GetChallenge(string token)
    {
        if (cache.TryGet(token, out var keyAuth))
        {
            return Ok(keyAuth);
        }

        return NotFound("Not found");
    }
}
