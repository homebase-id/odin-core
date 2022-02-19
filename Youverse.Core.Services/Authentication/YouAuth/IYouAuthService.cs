using System.Threading.Tasks;
using Youverse.Core.Cryptography;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthService
    {
        ValueTask<string> CreateAuthorizationCode(string initiator, string subject);
        ValueTask<(bool, byte[])> ValidateAuthorizationCodeRequest(string initiator, string subject, string authorizationCode);
        ValueTask<(bool, byte[])> ValidateAuthorizationCode(string initiator, string authorizationCode);
        ValueTask<(YouAuthSession, byte[]?)> CreateSession(string subject, SensitiveByteArray? xTokenHalfKey);
        ValueTask DeleteSession(string subject);
    }

}