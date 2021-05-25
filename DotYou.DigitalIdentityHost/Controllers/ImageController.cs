using System;
using System.IO;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Contacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers
{
    [ApiController]
    [Route("api/images")]
    //[Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class ImageController : ControllerBase
    {
        IContactService _contactService;

        public ImageController(IContactService contactService)
        {
            _contactService = contactService;
        }

        
        [HttpGet("avatar/{identifier}")]
        public IActionResult Get(string identifier)
        {
            //TODO: read from cache or contact list by identifier
            //return new JsonResult(new AvatarUri() {Uri = "/assets/unknown.png"});
            
            string root = $"wwwroot";
            string file = Path.Combine(root,"samples", $"{identifier}.jpg");
            string type = "image/jpeg";
            if (!System.IO.File.Exists(file))
            {
                //file = $"{root}{Path.PathSeparator}assets{Path.PathSeparator}unknown.png";
                file = Path.Combine(root, "assets","unknown.png");
                type = "image/png";
            }

            Byte[] b = System.IO.File.ReadAllBytes(file);
            var result = File(b, type);
            return result;
        }


    }
}
