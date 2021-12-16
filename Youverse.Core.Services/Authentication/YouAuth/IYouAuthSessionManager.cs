using System;
using System.Threading.Tasks;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthSessionManager
    {
        ValueTask<YouAuthSession> CreateSession(string subject);
        ValueTask<YouAuthSession?> LoadFromId(Guid id);
        ValueTask<YouAuthSession?> LoadFromSubject(string subject);
        ValueTask DeleteFromSubject(string subject);
    }
}