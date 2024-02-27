#nullable enable

using System;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;
using Odin.Core.Storage;

namespace Odin.Hosting.Controllers.Home.Service
{
    public class HomeRegistrationStorage
    {
        private readonly SingleKeyValueStorage _clientStorage;

        public HomeRegistrationStorage(TenantSystemStorage tenantSystemStorage)
        {
            const string homeClientContextKey = "7daac4aa-5088-4b46-96bd-47f03704dab4";
            _clientStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(homeClientContextKey));
        }

        public HomeAppClient? GetClient(Guid id)
        {
            var client = _clientStorage.Get<HomeAppClient>(id);
            return client;
        }

        public void SaveClient(HomeAppClient client)
        {
            if (null == client?.AccessRegistration?.Id)
            {
                throw new OdinClientException("Invalid client id");
            }

            _clientStorage.Upsert(client.AccessRegistration.Id, client);
        }

        public void DeleteClient(GuidId accessRegistrationId)
        {
            _clientStorage.Delete(accessRegistrationId);
        }
    }
}