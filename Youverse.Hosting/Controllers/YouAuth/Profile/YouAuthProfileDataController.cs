using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Profile;
using Youverse.Hosting.Authentication.YouAuth;

namespace Youverse.Hosting.Controllers.YouAuth.Profile
{
    [ApiController]
    [Route("/api/youauth/v1/profile")]
    [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
    public class YouAuthProfileDataController : Controller
    {
        private readonly IProfileAttributeManagementService _profileAttributeService;

        public YouAuthProfileDataController(IProfileAttributeManagementService profileAttributeService)
        {
            _profileAttributeService = profileAttributeService;
        }


        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            var attributes = await _profileAttributeService.GetAttributes(new PageOptions(pageNumber, pageSize));
            
            //Note: using object because: https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-polymorphism
            var collection = attributes.Results.Cast<object>();
            return new JsonResult(collection);
        }
    }
}