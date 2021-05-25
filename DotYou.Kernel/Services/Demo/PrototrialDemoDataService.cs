using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Contacts;
using DotYou.Types;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace DotYou.Kernel.Services.Demo
{
    public class PrototrialDemoDataService : DotYouServiceBase, IPrototrialDemoDataService
    {
        IContactService _contactService;
        
        public PrototrialDemoDataService(DotYouContext context,  ILogger logger, IContactService contactService) : base(context, logger, null, null)
        {
            _contactService = contactService;
        }

        public async Task<bool> AddDigitalIdentities()
        {

            if (this.Context.DotYouId != "frodobaggins.me")
            {
                await _contactService.Save(new Contact() { DotYouId = (DotYouIdentity)"frodobaggins.me", GivenName = "Frodo", Surname = "Baggins", Tag="Fellowship", PrimaryEmail="mail@frodobaggins.me" });
            }
            if (this.Context.DotYouId != "samwisegamgee.me")
            {
                await _contactService.Save(new Contact() {DotYouId = (DotYouIdentity) "samwisegamgee.me", GivenName = "Samwise", Surname = "Gamgee", Tag = "Fellowship", PrimaryEmail = "mail@samwisegamgee.me"});
            }
            
            if (this.Context.DotYouId != "gandalf.middleearth.life")
            {
                await _contactService.Save(new Contact() {DotYouId = (DotYouIdentity) "gandalf.middleearth.life", GivenName = "Olorin", Surname = "Maiar", Tag = "Fellowship", PrimaryEmail = "mail@gandalf.middleearth.life"});
            }
            
            if (this.Context.DotYouId != "arwen.youfoundation.id")
            {
                await _contactService.Save(new Contact() {DotYouId = (DotYouIdentity) "arwen.youfoundation.id", GivenName = "Awren", Surname = "Undomiel", Tag = "Fellowship", PrimaryEmail = "mail@arwen.youfoundation.id"});
            }
            
            if (this.Context.DotYouId != "odin.valhalla.com")
            {
                await _contactService.Save(new Contact() {DotYouId = (DotYouIdentity) "odin.valhalla.com", GivenName = "Odin", Surname = "", Tag = "Acquaintance", PrimaryEmail = "mail@frodobaggins.me"});
            }
            
            return true;
        }
    }
}