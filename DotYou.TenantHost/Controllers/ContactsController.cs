using System;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Types;
using Identity.Web.Services.Contacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;


namespace DotYou.TenantHost.Controllers
{
    [ApiController]
    [Route("api/contacts")]
    [Authorize(Policy = DotYouPolicyNames.MustOwnThisIdentity)]
    public class ContactsController : ControllerBase
    {
        IContactService _contactService;

        public ContactsController(IContactService contactService)
        {
            _contactService = contactService;
        }

        public async Task<IActionResult> DeleteContact(Guid id)
        {
            await _contactService.Delete(id);
            return new JsonResult(new NoResultResponse(true));

        }

        public async Task<PagedResult<Contact>> Find(string text,int pageNumber, int pageSize)
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

        public async Task<IActionResult> GetContact(Guid id)
        {
            var result = await _contactService.Get(id);
            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            return new JsonResult(result);
        }

        public async Task<IActionResult> GetContact(string domainName)
        {
            var result = await _contactService.GetByDomainName(domainName);
            if (result == null)
            {
                return new JsonResult(new NoResultResponse(true))
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            return new JsonResult(result);
        }

        public async Task<PagedResult<Contact>> GetContactsList(int pageNumber, int pageSize)
        {
            var result = await _contactService.GetContacts(new PageOptions(pageNumber, pageSize));
            return result;

        }

        public async Task<IActionResult> SaveContact(Contact contact)
        {
            await _contactService.Save(contact);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}
