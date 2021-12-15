using System.Threading.Tasks;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthService
    {
        ValueTask<string> CreateAuthorizationCode(string initiator, string subject);
        ValueTask<bool> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode);
        ValueTask<bool> ValidateAuthorizationCode(string initiator, string authorizationCode);
        ValueTask<YouAuthSession> CreateSession(string subject);
        ValueTask DeleteSession(string subject);
    }

}