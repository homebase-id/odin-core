using System;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthRegistrationStorage
    {
        YouAuthRegistration? LoadFromId(Guid id);
        YouAuthRegistration? LoadFromSubject(string subject);
        void Save(YouAuthRegistration registration);
        void Delete(YouAuthRegistration registration);
    }
}