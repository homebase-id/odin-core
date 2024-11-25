#nullable enable

using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.SQLite;

namespace Odin.Hosting.Controllers.Home.Service
{
    public class HomeRegistrationStorage
    {
        private readonly TableKeyValue _tblKeyValue;
        private readonly SingleKeyValueStorage _clientStorage;

        public HomeRegistrationStorage( TableKeyValue tblKeyValue)
        {
            _tblKeyValue = tblKeyValue;
            const string homeClientContextKey = "7daac4aa-5088-4b46-96bd-47f03704dab4";
            _clientStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(homeClientContextKey));
        }

        public async Task<HomeAppClient?> GetClientAsync(Guid id)
        {
            
            var client = await _clientStorage.GetAsync<HomeAppClient>(_tblKeyValue, id);
            return client;
        }

        public async Task SaveClientAsync(HomeAppClient client)
        {
            
            if (null == client?.AccessRegistration?.Id)
            {
                throw new OdinClientException("Invalid client id");
            }

            await _clientStorage.UpsertAsync(_tblKeyValue, client.AccessRegistration.Id, client);
        }

        public async Task DeleteClientAsync(GuidId accessRegistrationId)
        {
            
            await _clientStorage.DeleteAsync(_tblKeyValue, accessRegistrationId);
        }
    }
}