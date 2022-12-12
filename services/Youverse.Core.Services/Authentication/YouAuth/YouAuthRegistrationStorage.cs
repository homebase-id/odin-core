using System;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public class YouAuthRegistrationStorage : IYouAuthRegistrationStorage
    {
        private readonly ITenantSystemStorage _tenantSystemStorage;

        public YouAuthRegistrationStorage(ITenantSystemStorage tenantSystemStorage)
        {
            _tenantSystemStorage = tenantSystemStorage;
        }
        
        //

        public YouAuthRegistration? LoadFromSubject(string subject)
        {
            return _tenantSystemStorage.SingleKeyValueStorage.Get<YouAuthRegistration>(GuidId.FromString(subject));
        }

        //

        public void Save(YouAuthRegistration registration)
        {
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(GuidId.FromString(registration.Subject), registration);
        }

        //

        public void Delete(YouAuthRegistration registration)
        {
            _tenantSystemStorage.SingleKeyValueStorage.Delete(GuidId.FromString(registration.Subject));
            
            //TODO: delete clients as well
        }

        public YouAuthClient? GetYouAuthClient(Guid id)
        {
            var client = _tenantSystemStorage.SingleKeyValueStorage.Get<YouAuthClient>(id);
            return client;
        }

        public void SaveClient(YouAuthClient client)
        {
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(client.Id, client);
        }
    }
}