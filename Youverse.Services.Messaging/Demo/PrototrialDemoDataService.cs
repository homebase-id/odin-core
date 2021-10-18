using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Services.Circle;
using DotYou.Kernel.Services.Contacts;
using DotYou.Kernel.Services.Owner.Data;
using DotYou.Types;
using DotYou.Types.Circle;
using DotYou.Types.DataAttribute;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Demo
{
    public class PrototrialDemoDataService : DotYouServiceBase, IPrototrialDemoDataService
    {
        private readonly IProfileService _profileService;
        private readonly IOwnerDataAttributeManagementService _admin;
        private readonly ICircleNetworkRequestService _circleNetworkService;

        public PrototrialDemoDataService(DotYouContext context, ILogger logger, IProfileService profileService, IOwnerDataAttributeManagementService admin, ICircleNetworkRequestService circleNetworkService) : base(context, logger, null, null)
        {
            _profileService = profileService;
            _admin = admin;
            _circleNetworkService = circleNetworkService;
        }

        public async Task<bool> AddDigitalIdentities()
        {
            if (IsFrodo)
            {
                //sam was left out intentionally; like a blank page.  actually just so we can send a request and fulfill it.
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "gandalf.middleearth.life", Name = new NameAttribute() {Personal = "Olorin", Surname = "Maiar"}});
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "odin.valhalla.com", Name = new NameAttribute() {Personal = "Odin", Surname = "Rune Bringer"}});
            }

            if (IsSam)
            {
                //frodo was left out intentionally; like a blank page.  actually just so we can send a request and fulfill it.
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "gandalf.middleearth.life", Name = new NameAttribute() {Personal = "Olorin", Surname = "Maiar"}});
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "odin.valhalla.com", Name = new NameAttribute() {Personal = "Odin", Surname = "Rune Bringer"}});
            }

            if (IsGandalf)
            {
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "frodobaggins.me", Name = new NameAttribute() {Personal = "Frodo", Surname = "Baggins"}});
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "samwisegamgee.me", Name = new NameAttribute() {Personal = "Samwise", Surname = "Gamgee"}});
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "odin.valhalla.com", Name = new NameAttribute() {Personal = "Odin", Surname = "Rune Bringer"}});
            }

            if (IsOdin)
            {
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "frodobaggins.me", Name = new NameAttribute() {Personal = "Frodo", Surname = "Baggins"}});
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "gandalf.middleearth.life", Name = new NameAttribute() {Personal = "Olorin", Surname = "Maiar"}});
                await _profileService.Save(new DotYouProfile() {DotYouId = (DotYouIdentity) "samwisegamgee.me", Name = new NameAttribute() {Personal = "Samwise", Surname = "Gamgee"}});
            }

            return true;
        }

        public async Task<bool> AddConnectionRequests()
        {
            // var toGandalf = new ConnectionRequestHeader()
            // {
            //     Id = Guid.NewGuid(),
            //     Message = "Connect with me!... and yes it's secret and safe",
            //     Recipient = (DotYouIdentity) "gandalf.middleearth.life"
            // };

            if (IsFrodo)
            {
                var toSam = new ConnectionRequestHeader()
                {
                    Id = Guid.NewGuid(),
                    Message = "Connect with me... we've got some walking to do",
                    Recipient = (DotYouIdentity) "samwisegamgee.me"
                };

                await _circleNetworkService.SendConnectionRequest(toSam);
            }

            if (IsSam)
            {
                var toFrodo = new ConnectionRequestHeader()
                {
                    Id = Guid.NewGuid(),
                    Message = "Connect with me..lets go for a long stroll",
                    Recipient = (DotYouIdentity) "frodobaggins.me"
                };

                await _circleNetworkService.SendConnectionRequest(toFrodo);
            }

            if (IsGandalf)
            {
                var toFrodo = new ConnectionRequestHeader()
                {
                    Id = Guid.NewGuid(),
                    Message = "Connect with me..lets go for a long stroll",
                    Recipient = (DotYouIdentity) "frodobaggins.me"
                };

                await _circleNetworkService.SendConnectionRequest(toFrodo);

                var toSam = new ConnectionRequestHeader()
                {
                    Id = Guid.NewGuid(),
                    Message = "Connect with me... we've got some walking to do",
                    Recipient = (DotYouIdentity) "samwisegamgee.me"
                };

                await _circleNetworkService.SendConnectionRequest(toSam);
            }

            if (IsOdin)
            {
                var toGandalf = new ConnectionRequestHeader()
                {
                    Id = Guid.NewGuid(),
                    Message = "Let us speak of Pantheons",
                    Recipient = (DotYouIdentity) "gandalf.middleearth.life"
                };

                await _circleNetworkService.SendConnectionRequest(toGandalf);
            }

            return true;
        }

        public async Task SetProfiles()
        {
            NameAttribute primaryName = null;
            OwnerProfile publicProfile = null;
            OwnerProfile privateProfile = null;

            if (IsFrodo)
            {
                primaryName = new NameAttribute() {Personal = "Frodo", Surname = "Baggins"};
                publicProfile = new OwnerProfile()
                {
                    Name = new NameAttribute()
                    {
                        Personal = "Frodo",
                        Surname = "Underhill",
                        Suffix = ""
                    },
                    Photo = new ProfilePicAttribute()
                    {
                        ProfilePic = "https://i2.wp.com/a1.ec-images.myspacecdn.com/images01/20/849847696a4b587b3e848eddd93c207e/l.jpg?zoom=2"
                    }
                };

                privateProfile = new OwnerProfile()
                {
                    Name = new NameAttribute()
                    {
                        Personal = "Frodo",
                        Surname = "Baggins",
                        Suffix = "Ring-Bearer"
                    },
                    Photo = new ProfilePicAttribute()
                    {
                        ProfilePic = "https://img.huffingtonpost.com/asset/5d022ceb240000300f8d12bb.jpeg?ops=scalefit_720_noupscale"
                    }
                };
            }

            if (IsSam)
            {
                primaryName = new NameAttribute() {Personal = "Samwise", Surname = "Gamgee"};

                publicProfile = new OwnerProfile()
                {
                    Name = new NameAttribute()
                    {
                        Personal = "Samwise",
                        Surname = "Gamgee",
                        Suffix = "The Brave"
                    },
                    Photo = new ProfilePicAttribute()
                    {
                        ProfilePic = "https://static.wikia.nocookie.net/middle-earth-film-saga/images/5/52/Sam_TTT_profile.png/revision/latest?cb=20190727211735"
                    }
                };

                privateProfile = new OwnerProfile()
                {
                    Name = new NameAttribute()
                    {
                        Personal = "Samwise",
                        Surname = "Gamgee",
                        Suffix = "Ring-bearer"
                    },
                    Photo = new ProfilePicAttribute()
                    {
                        ProfilePic= "https://miro.medium.com/max/640/0*tEUhip6Bfy-dDZSD"
                    }
                };
            }

            if (IsGandalf)
            {
                primaryName = new NameAttribute() {Personal = "Gandalf", Surname = "the White"};

                publicProfile = new OwnerProfile()
                {
                    Name = new NameAttribute()
                    {
                        Personal = "Gandalf",
                        Surname = "White",
                        Suffix = ""
                    },
                    Photo = new ProfilePicAttribute()
                    {
                        ProfilePic= "data:image/jpeg;base64,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"
                    }
                };

                privateProfile = new OwnerProfile()
                {
                    Name = new NameAttribute()
                    {
                        Personal = "Gandalf",
                        Surname = "White",
                        Suffix = "Maiar of Valinor"
                    },
                    Photo = new ProfilePicAttribute()
                    {
                        ProfilePic= "https://static.wikia.nocookie.net/lotr/images/7/70/Gandalf%3B_The_White.jpg/revision/latest/scale-to-width-down/250?cb=20140110102343"
                    }
                };
            }

            if (IsOdin)
            {
                primaryName = new NameAttribute() {Personal = "Odin", Surname = "_"};

                publicProfile = new OwnerProfile()
                {
                    Name = new NameAttribute()
                    {
                        Personal = "Odin",
                        Surname = "",
                        Suffix = ""
                    },
                    Photo = new ProfilePicAttribute()
                    {
                        ProfilePic = "https://static.wikia.nocookie.net/da27b848-42dd-4a68-933a-2ff32ed33895/scale-to-width/755"
                    }
                };

                privateProfile = new OwnerProfile()
                {
                    Name = new NameAttribute()
                    {
                        Personal = "Odin",
                        Surname = "",
                        Suffix = "Rune Bringer"
                    },
                    Photo = new ProfilePicAttribute()
                    {
                        ProfilePic = "https://www.majesticdragonfly.com/image/cache/catalog/veronese/odin-with-ravens-norse-god-bust-statue-WU77529A4-900x900.jpg"
                    }
                };
            }

            await _admin.SavePrimaryName(primaryName);
            await _admin.SavePublicProfile(publicProfile);
            await _admin.SaveConnectedProfile(privateProfile);
        }


        private bool IsFrodo => this.Context.HostDotYouId == "frodobaggins.me";
        private bool IsSam => this.Context.HostDotYouId == "samwisegamgee.me";
        private bool IsGandalf => this.Context.HostDotYouId == "gandalf.middleearth.life";
        private bool IsOdin => this.Context.HostDotYouId == "odin.valhalla.com";
    }
}