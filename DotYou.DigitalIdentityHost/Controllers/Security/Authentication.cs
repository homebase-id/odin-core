using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Security
{
    [ApiController]
    [Route("/api/authentication")]
    public class Authentication : Controller
    {
        // GET
        [HttpPost("login")]
        public async Task<IActionResult> Login(string password)
        {
            return Ok();
        }
    }
}