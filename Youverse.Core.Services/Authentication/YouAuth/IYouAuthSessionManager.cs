using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Exchange;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthSessionManager
    {
        ValueTask<YouAuthSession> CreateSession(string subject, ExchangeRegistration token);
        ValueTask<YouAuthSession?> LoadFromId(Guid id);
        ValueTask<YouAuthSession?> LoadFromSubject(string subject);
        ValueTask DeleteFromSubject(string subject);
    }
}