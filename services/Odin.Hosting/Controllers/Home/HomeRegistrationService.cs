#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Membership.Connections;

namespace Odin.Hosting.Controllers.Home
{
    public sealed class HomeRegistrationService 
    {
        private readonly HomeRegistrationStorage _homeRegistrationStorage;

        public HomeRegistrationService(HomeRegistrationStorage homeRegistrationStorage)
        {
            _homeRegistrationStorage = homeRegistrationStorage;
        }

        public ValueTask<YouAuthRegistration?> LoadFromSubject(string subject)
        {
            var session = _homeRegistrationStorage.LoadFromSubject(subject);

            if (session != null)
            {
                _homeRegistrationStorage.Delete(session);
                session = null;
            }

            return new ValueTask<YouAuthRegistration?>(session);
        }

        public ValueTask DeleteFromSubject(string subject)
        {
            var session = _homeRegistrationStorage.LoadFromSubject(subject);
            if (session != null)
            {
                _homeRegistrationStorage.Delete(session);
            }

            return new ValueTask();
        }
        
        //
        
        
    }
}