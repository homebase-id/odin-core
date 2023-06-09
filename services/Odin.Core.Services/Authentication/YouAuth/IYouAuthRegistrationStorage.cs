#nullable enable

using System;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthRegistrationStorage
    {
        YouAuthRegistration? LoadFromSubject(string subject);
        void Save(YouAuthRegistration registration);
        void Delete(YouAuthRegistration registration);

        void SaveClient(YouAuthClient client);

        YouAuthClient? GetYouAuthClient(Guid id);
    }
}