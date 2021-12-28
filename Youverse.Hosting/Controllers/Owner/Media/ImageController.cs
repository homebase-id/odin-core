using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Profile;
using Youverse.Core.Util;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Media
{
    [ApiController]
    [Route("api/images")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.SchemeName)]
    public class ImageController : ControllerBase
    {
        IProfileService _profileService;

        public ImageController(IProfileService profileService)
        {
            _profileService = profileService;
        }

        
        [HttpGet("avatar/{identifier}")]
        public IActionResult GetAvatar(string identifier)
        {
            //TODO: read from cache or contact list by identifier
            //return new JsonResult(new AvatarUri() {Uri = "/assets/unknown.png"});
            
            string root = $"wwwroot";
            string file = PathUtil.Combine(root,"samples", $"{identifier}.jpg");
            string type = "image/jpeg";
            if (!System.IO.File.Exists(file))
            {
                //file = $"{root}{Path.PathSeparator}assets{Path.PathSeparator}unknown.png";
                file = PathUtil.Combine(root, "assets","unknown.png");
                type = "image/png";
            }

            Byte[] b = System.IO.File.ReadAllBytes(file);
            var result = File(b, type);
            return result;
        }


    }
}
