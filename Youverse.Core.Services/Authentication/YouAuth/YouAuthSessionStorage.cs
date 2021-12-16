using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public class YouAuthSessionStorage : IYouAuthSessionStorage
    {
        private const string StorageCollectionName = "Session";
        private readonly ISystemStorage _systemStorage;

        public YouAuthSessionStorage(ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
            _systemStorage.WithTenantSystemStorage<YouAuthSession>(StorageCollectionName, s => s.EnsureIndex(k => k.Subject, true));
        }

        //

        public YouAuthSession? LoadFromId(Guid id)
        {
            var task = _systemStorage.WithTenantSystemStorageReturnSingle<YouAuthSession?>(StorageCollectionName, s => s.FindOne(p => p.Id == id));
            return task.GetAwaiter().GetResult(); // litedb is blocking, no reason to keep up the charade
        }

        //

        public YouAuthSession? LoadFromSubject(string subject)
        {
            var task = _systemStorage.WithTenantSystemStorageReturnSingle<YouAuthSession?>(StorageCollectionName, s => s.FindOne(p => p.Subject == subject));
            return task.GetAwaiter().GetResult(); // litedb is blocking, no reason to keep up the charade
        }

        //

        public void Save(YouAuthSession session)
        {
            _systemStorage.WithTenantSystemStorage<YouAuthSession>(StorageCollectionName, s => s.Save(session));
        }

        //

        public void Delete(YouAuthSession session)
        {
            _systemStorage.WithTenantSystemStorage<YouAuthSession>(StorageCollectionName, s => s.Delete(session.Id));
        }

        //

    }
}
