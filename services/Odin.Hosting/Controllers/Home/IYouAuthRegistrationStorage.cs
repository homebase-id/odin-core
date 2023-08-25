#nullable enable

using System;
using Odin.Core.Services.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.Home
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