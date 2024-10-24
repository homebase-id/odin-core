#nullable enable

using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;

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

        public async Task<HomeAppClient?> GetClientAsync(Guid id)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var client = await _clientStorage.GetAsync<HomeAppClient>(db, id);
            return client;
        }

        public async Task SaveClientAsync(HomeAppClient client)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            if (null == client?.AccessRegistration?.Id)
            {
                throw new OdinClientException("Invalid client id");
            }

            await _clientStorage.UpsertAsync(db, client.AccessRegistration.Id, client);
        }

        public async Task DeleteClientAsync(GuidId accessRegistrationId)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            await _clientStorage.DeleteAsync(db, accessRegistrationId);
        }
    }
}