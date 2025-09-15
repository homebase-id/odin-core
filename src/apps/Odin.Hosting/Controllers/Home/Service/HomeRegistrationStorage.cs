#nullable enable

using System;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Hosting.Controllers.Home.Service
{
    public class HomeRegistrationStorage
    {
        private const string HomeClientContextKey = "7daac4aa-5088-4b46-96bd-47f03704dab4";
        private static readonly SingleKeyValueStorage ClientStorage = TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(HomeClientContextKey));

        private readonly TableKeyValueCached _tblKeyValue;

        public HomeRegistrationStorage(TableKeyValueCached tblKeyValue)
        {
            _tblKeyValue = tblKeyValue;
        }

        public async Task<HomeAppClient?> GetClientAsync(Guid id)
        {
            
            var client = await ClientStorage.GetAsync<HomeAppClient>(_tblKeyValue, id);
            return client;
        }

        public async Task SaveClientAsync(HomeAppClient client)
        {
            
            if (null == client?.AccessRegistration?.Id)
            {
                throw new OdinClientException("Invalid client id");
            }

            await ClientStorage.UpsertAsync(_tblKeyValue, client.AccessRegistration.Id, client);
        }

        public async Task DeleteClientAsync(GuidId accessRegistrationId)
        {
            
            await ClientStorage.DeleteAsync(_tblKeyValue, accessRegistrationId);
        }
    }
}