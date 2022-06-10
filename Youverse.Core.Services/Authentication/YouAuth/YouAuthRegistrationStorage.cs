using System;
using Youverse.Core.Services.Base;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public class YouAuthRegistrationStorage : IYouAuthRegistrationStorage
    {
        private const string StorageCollectionName = "youauthreg";
        private readonly ISystemStorage _systemStorage;

        public YouAuthRegistrationStorage(ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
            _systemStorage.WithTenantSystemStorage<YouAuthRegistration>(StorageCollectionName, s => s.EnsureIndex(k => k.Subject, true));
        }

        //

        public YouAuthRegistration? LoadFromId(Guid id)
        {
            var task = _systemStorage.WithTenantSystemStorageReturnSingle<YouAuthRegistration?>(StorageCollectionName, s => s.FindOne(p => p.Id == id));
            return task.GetAwaiter().GetResult(); // litedb is blocking, no reason to keep up the charade
        }

        //

        public YouAuthRegistration? LoadFromSubject(string subject)
        {
            var task = _systemStorage.WithTenantSystemStorageReturnSingle<YouAuthRegistration?>(StorageCollectionName, s => s.FindOne(p => p.Subject == subject));
            return task.GetAwaiter().GetResult(); // litedb is blocking, no reason to keep up the charade
        }

        //

        public void Save(YouAuthRegistration registration)
        {
            _systemStorage.WithTenantSystemStorage<YouAuthRegistration>(StorageCollectionName, s => s.Save(registration));
        }

        //

        public void Delete(YouAuthRegistration registration)
        {
            _systemStorage.WithTenantSystemStorage<YouAuthRegistration>(StorageCollectionName, s => s.Delete(registration.Id));
        }

        //

    }
}