using System;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Contacts;
using DotYou.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers
{
    [ApiController]
    [Route("api/connections")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class HumanConnectionProfileController : ControllerBase
    {
        IHumanConnectionProfileService _profile;

        public HumanConnectionProfileController(IHumanConnectionProfileService profile)
        {
            _profile = profile;
        }


        [HttpGet("find")]
        public async Task<PagedResult<HumanConnectionProfile>> Find(string text, int pageNumber, int pageSize)
        {
            string q = text.ToLower();
            Expression<Func<HumanConnectionProfile, bool>> predicate;
           
            predicate = c =>
                c.GivenName.ToLower().Contains(q) ||
                c.Surname.ToLower().Contains(q) ||
                c.DotYouId.Id.Contains(q);

            var results = await _profile.Find(predicate, new PageOptions(pageNumber, pageSize));

            return results;
        }

        [HttpGet("{dotYouId}")]
        public async Task<IActionResult> GetContactByDomainName(string dotYouId)
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

        [HttpGet]
        public async Task<PagedResult<HumanConnectionProfile>> GetContactsList(int pageNumber, int pageSize)
        {
            var result = await _profile.GetConnections(new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpPost]
        public async Task<IActionResult> Save(HumanConnectionProfile humanConnectionProfile)
        {
            await _profile.Save(humanConnectionProfile);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpDelete("{dotYouId}")]
        public async Task<IActionResult> Delete(string dotYouId)
        {
            await _profile.Delete((DotYouIdentity) dotYouId);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}