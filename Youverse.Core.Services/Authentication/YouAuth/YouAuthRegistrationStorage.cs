using System;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public class YouAuthRegistrationStorage : IYouAuthRegistrationStorage
    {
        private const string _regPrefix = "yrg";
        private const string _clientPrefix = "yac";
        private readonly ISystemStorage _systemStorage;

        public YouAuthRegistrationStorage(ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
        }
        
        //

        public YouAuthRegistration? LoadFromSubject(string subject)
        {
            return _systemStorage.SingleKeyValueStorage.Get<YouAuthRegistration>(ByteArrayId.FromString(subject), _regPrefix);
        }

        //

        public void Save(YouAuthRegistration registration)
        {
            _systemStorage.SingleKeyValueStorage.Upsert(ByteArrayId.FromString(registration.Subject), registration, _regPrefix);
        }

        //

        public void Delete(YouAuthRegistration registration)
        {
            _systemStorage.SingleKeyValueStorage.Delete(ByteArrayId.FromString(registration.Subject), _regPrefix);
        }

        public YouAuthClient? GetYouAuthClient(Guid id)
        {
            var client = _systemStorage.SingleKeyValueStorage.Get<YouAuthClient>(id, _clientPrefix);
            return client;
        }

        public void SaveClient(YouAuthClient client)
        {
            _systemStorage.SingleKeyValueStorage.Upsert(client.Id, client, _clientPrefix);
        }
    }
}