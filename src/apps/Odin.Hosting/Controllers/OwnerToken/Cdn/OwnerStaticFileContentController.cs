using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Optimization.Cdn;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Cdn
{
    [ApiController]
    [Route(OwnerApiPathConstants.CdnV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerStaticFileContentController : StaticFileContentPublishControllerBase
    {
        private readonly StaticFileContentService _staticFileContentService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public OwnerStaticFileContentController(
            StaticFileContentService staticFileContentService,
            TenantSystemStorage tenantSystemStorage) : base(staticFileContentService)
        {
            _staticFileContentService = staticFileContentService;
            _tenantSystemStorage = tenantSystemStorage;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCdn })]
        [HttpPost("profileimage")]
        public async Task<IActionResult> PublishPublicProfileImage([FromBody] PublishPublicProfileImageRequest request)
        {
            await _staticFileContentService.PublishProfileImage(request.Image64, request.ContentType);
            return NoContent();
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCdn })]
        [HttpPost("profilecard")]
        public async Task<IActionResult> PublishPublicProfileCard([FromBody] PublishPublicProfileCardRequest request)
        {
            await _staticFileContentService.PublishProfileCard(request.ProfileCardJson);
            return NoContent();
        }
    }
}