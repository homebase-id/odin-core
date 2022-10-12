using System;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public class YouAuthRegistrationStorage : IYouAuthRegistrationStorage
    {
        private readonly ISystemStorage _systemStorage;

        public YouAuthRegistrationStorage(ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }
        
        //

        public YouAuthRegistration? LoadFromSubject(string subject)
        {
            return _systemStorage.SingleKeyValueStorage.Get<YouAuthRegistration>(GuidId.FromString(subject));
        }

        //

        public void Save(YouAuthRegistration registration)
        {
            _systemStorage.SingleKeyValueStorage.Upsert(GuidId.FromString(registration.Subject), registration);
        }

        //

        public void Delete(YouAuthRegistration registration)
        {
            _systemStorage.SingleKeyValueStorage.Delete(GuidId.FromString(registration.Subject));
            
            //TODO: delete clients as well
        }

        public YouAuthClient? GetYouAuthClient(Guid id)
        {
            var client = _systemStorage.SingleKeyValueStorage.Get<YouAuthClient>(id);
            return client;
        }

        public void SaveClient(YouAuthClient client)
        {
            _systemStorage.SingleKeyValueStorage.Upsert(client.Id, client);
        }
    }
}