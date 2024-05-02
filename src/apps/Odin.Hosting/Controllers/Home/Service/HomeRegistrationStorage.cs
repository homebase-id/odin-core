#nullable enable

using System;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;

namespace Odin.Hosting.Controllers.Home.Service
{
    public class HomeRegistrationStorage
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly SingleKeyValueStorage _clientStorage;

        public HomeRegistrationStorage(TenantSystemStorage tenantSystemStorage)
        {
            _tenantSystemStorage = tenantSystemStorage;
            const string homeClientContextKey = "7daac4aa-5088-4b46-96bd-47f03704dab4";
            _clientStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(homeClientContextKey));
        }

        public HomeAppClient? GetClient(Guid id, DatabaseConnection cn)
        {
            var client = _clientStorage.Get<HomeAppClient>(cn, id);
            return client;
        }

        public void SaveClient(HomeAppClient client, DatabaseConnection cn)
        {
            if (null == client?.AccessRegistration?.Id)
            {
                throw new OdinClientException("Invalid client id");
            }

            _clientStorage.Upsert(cn, client.AccessRegistration.Id, client);
        }

        public void DeleteClient(GuidId accessRegistrationId, DatabaseConnection cn)
        {
            _clientStorage.Delete(cn, accessRegistrationId);
        }
    }
}