using System;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Profile;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Circles
{
    [ApiController]
    [Route("api/profiles")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.SchemeName)]
    public class HumanConnectionProfileController : ControllerBase
    {
        IProfileService _profile;

        public HumanConnectionProfileController(IProfileService profile)
        {
            _profile = profile;
        }


        [HttpGet("find")]
        public async Task<PagedResult<DotYouProfile>> Find(string text, int pageNumber, int pageSize)
        {
            string q = text.ToLower();
            Expression<Func<DotYouProfile, bool>> predicate;
           
            predicate = c =>
                c.Name.Personal.ToLower().Contains(q) ||
                c.Name.Surname.ToLower().Contains(q) ||
                c.DotYouId.ToString().Contains(q);

            var results = await _profile.Find(predicate, new PageOptions(pageNumber, pageSize));

            return results;
        }

        [HttpGet("{dotYouId}")]
        public async Task<IActionResult> GetProfile(string dotYouId)
        {
            var result = await _profile.Get((DotYouIdentity) dotYouId);
            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int) HttpStatusCode.NotFound
                };
            }

            return new JsonResult(result);
        }

        // [HttpPost]
        // public async Task<IActionResult> Save(HumanProfile humanProfile)
        // {
        //     await _profile.Save(humanProfile);
        //     return new JsonResult(new NoResultResponse(true));
        // }
        //
        // [HttpDelete("{dotYouId}")]
        // public async Task<IActionResult> Delete(string dotYouId)
        // {
        //     await _profile.Delete((DotYouIdentity) dotYouId);
        //     return new JsonResult(new NoResultResponse(true));
        // }
    }
}