#nullable enable

using System;
using Odin.Core.Exceptions;
using Odin.Core.Services.Base;

namespace Odin.Hosting.Controllers.Home.Service
{
    public class HomeRegistrationStorage
    {
        private readonly TenantSystemStorage _tenantSystemStorage;

        public HomeRegistrationStorage(TenantSystemStorage tenantSystemStorage)
        {
            _tenantSystemStorage = tenantSystemStorage;
        }

        public HomeAppClient? GetClient(Guid id)
        {
            var client = _tenantSystemStorage.SingleKeyValueStorage.Get<HomeAppClient>(id);
            return client;
        }

        public void SaveClient(HomeAppClient client)
        {
            if (null == client?.AccessRegistration?.Id)
            {
                throw new OdinClientException("Invalid client id");
            }
            
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(client.AccessRegistration.Id, client);
        }

        public void DeleteClient(GuidId accessRegistrationId)
        {
            _tenantSystemStorage.SingleKeyValueStorage.Delete(accessRegistrationId);
        }
    }
}