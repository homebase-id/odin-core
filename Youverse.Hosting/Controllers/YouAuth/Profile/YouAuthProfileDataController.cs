using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Core.Identity.DataAttribute;


namespace Youverse.Hosting.Controllers.YouAuth.Profile
{
    [ApiController]
    [Route("/api/youauth/v1/profile")]
    //TODO: add in Authorize tag when merging with seb
    public class YouAuthProfileDataController : Controller
    {
        public YouAuthProfileDataController()
        {
        }
        
        
        [HttpGet("{attributeId}")]
        public async Task<IActionResult> Get(Guid attributeId)
        {
            return new JsonResult("");
        }

        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] int pageNumber, [FromQuery] int pageSize)
        {
            return new JsonResult("");
        }

        /// <summary>
        /// Returns the public attributes for this digital identity
        /// </summary>
        /// <returns></returns>
        [HttpGet("public")]
        public async Task<IActionResult> GetPublicProfileAttributeSet()
        {
            var collection = new List<BaseAttribute>()
            {
                new NameAttribute()
                {
                    Personal = "Frodo",
                    Surname = "Baggins"
                },

                new FaceBookAttribute()
                {
                    FaceBook = "frodo.baggins"
                },

                new TwitterAttribute()
                {
                    Twitter = "@captain_underhill"
                }
            };

            return new JsonResult(collection);
        }
    }
}