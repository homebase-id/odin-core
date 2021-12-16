using System;
using System.Threading.Tasks;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthSessionStorage
    {
        YouAuthSession? LoadFromId(Guid id);
        YouAuthSession? LoadFromSubject(string subject);
        void Save(YouAuthSession session);
        void Delete(YouAuthSession session);
    }
}