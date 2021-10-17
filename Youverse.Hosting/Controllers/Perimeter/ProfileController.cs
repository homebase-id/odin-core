using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Owner.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter
{
    /// <summary>
    /// Controller making available the DI owner's profile data based on the level
    /// of access the caller is given
    /// </summary>
    [ApiController]
    [Route("api/perimeter/profile")]
    [Authorize(Policy = DotYouPolicyNames.MustBeIdentified)]
    public class ProfileController : ControllerBase
    {
        private readonly IOwnerDataAttributeReaderService _reader;

        public ProfileController(IOwnerDataAttributeReaderService reader)
        {
            _reader = reader;
        }
        
        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            //TODO: determine if we map the avatar uri to one that sends the request back through the user's DI
            var profile = await _reader.GetProfile();
            return new JsonResult(profile);
        }
    }
}