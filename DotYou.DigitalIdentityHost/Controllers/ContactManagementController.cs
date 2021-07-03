using System;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Contacts;
using DotYou.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace DotYou.TenantHost.Controllers
{
    [ApiController]
    [Route("api/contacts")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class ContactManagementController : ControllerBase
    {
        IContactService _contactService;

        public ContactManagementController(IContactService contactService)
        {
            _contactService = contactService;
        }


        [HttpGet("find")]
        public async Task<PagedResult<Contact>> Find(string text, int pageNumber, int pageSize)
        {
            string q = text.ToLower();
            Expression<Func<Contact, bool>> predicate;
            /*
            if (mustHaveYFIdentity)
            {
                predicate = c => (c.GivenName.ToLower().Contains(q) ||
                             c.Surname.ToLower().Contains(q) ||
                             c.DotYouId.Value.Id.Contains(q))
                             && c.DotYouId.HasValue;
            }
            else
            {
                predicate = c =>
                             c.GivenName.ToLower().Contains(q) ||
                             c.Surname.ToLower().Contains(q) ||
                             c.DotYouId.Value.Id.Contains(q);
            }
            */

            predicate = c =>
                c.GivenName.ToLower().Contains(q) ||
                c.Surname.ToLower().Contains(q) ||
                c.DotYouId.Value.Id.Contains(q);

            var results = await _contactService.FindContacts(predicate, new PageOptions(pageNumber, pageSize));

            return results;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetContactById(Guid id)
        {
            var result = await _contactService.Get(id);
            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int) HttpStatusCode.NotFound
                };
            }

            return new JsonResult(result);
        }

        [HttpGet("{domainName}")]
        public async Task<IActionResult> GetContactByDomainName(string domainName)
        {
            var result = await _contactService.GetByDotYouId(domainName);
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
        public async Task<PagedResult<Contact>> GetContactsList(bool connectedContactsOnly, int pageNumber, int pageSize)
        {
            var result = await _contactService.GetContacts(new PageOptions(pageNumber, pageSize), connectedContactsOnly);
            return result;
        }

        [HttpPost]
        public async Task<IActionResult> SaveContact(Contact contact)
        {
            await _contactService.Save(contact);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteContact(Guid id)
        {
            await _contactService.Delete(id);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}