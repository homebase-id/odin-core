using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Optimization.Cdn;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Cdn
{
    [ApiController]
    [Route(OwnerApiPathConstants.CdnV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerStaticFileContentController : ControllerBase
    {
        private readonly StaticFileContentService _staticFileContentService;

        public OwnerStaticFileContentController(StaticFileContentService staticFileContentService)
        {
            _staticFileContentService = staticFileContentService;
        }

        /// <summary>
        /// Creates a static file which contents match the query params.  Accessible to the public
        /// as it will only contain un-encrypted content targeted at Anonymous users
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCdn })]
        [HttpPost("publish")]
        public async Task<StaticFilePublishResult> PublishBatch([FromBody] PublishStaticFileRequest request)
        {
            var publishResult = await _staticFileContentService.Publish(request.Filename, request.Config, request.Sections);
            return publishResult;
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

    public class PublishPublicProfileCardRequest
    {
        /// <summary>
        /// The json string of the profile card.
        /// </summary>
        public string ProfileCardJson { get; set; }
    }

    public class PublishPublicProfileImageRequest
    {
        /// <summary>
        /// Base64 encoded byte array of the image
        /// </summary>
        public string Image64 { get; set; }

        /// <summary>
        ///  The mime-type
        /// </summary>
        public string ContentType { get; set; }
    }
}