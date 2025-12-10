using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.Authentication.Owner;
using Odin.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Cdn
{
    [ApiController]
    [Route(OwnerApiPathConstants.CdnV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerStaticFileContentController : StaticFileContentPublishControllerBase
    {
        private readonly StaticFileContentService _staticFileContentService;


        public OwnerStaticFileContentController(
            StaticFileContentService staticFileContentService) : base(staticFileContentService)
        {
            _staticFileContentService = staticFileContentService;
            
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCdn })]
        [HttpPost("profileimage")]
        public async Task<IActionResult> PublishPublicProfileImage([FromBody] PublishPublicProfileImageRequest request)
        {
            if (string.IsNullOrEmpty(request.Image64))
            {
                return BadRequest("Image64 is required");
            }
            if (request.Image64.Length > Constants.OneMiB)
            {
                return BadRequest("Image64 is too big (> 1 MiB)");
            }

            await _staticFileContentService.PublishProfileImageAsync(request.Image64, request.ContentType);
            return NoContent();
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCdn })]
        [HttpPost("profilecard")]
        public async Task<IActionResult> PublishPublicProfileCard([FromBody] PublishPublicProfileCardRequest request)
        {
            await _staticFileContentService.PublishProfileCardAsync(request.ProfileCardJson);
            return NoContent();
        }
    }
}