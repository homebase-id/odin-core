using System;
using System.Collections.Generic;
using System.Threading.Tasks;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public interface IYouAuthAuthorizationCodeManager
    {
        ValueTask<string> CreateAuthorizationCode(string initiator, string subject);
        ValueTask<bool> ValidateAuthorizationCode(string initiator, string authorizationCode);
    }

    public sealed class YouAuthAuthorizationCodeManager : IYouAuthAuthorizationCodeManager
    {
        private readonly TimeSpan _authorizationCodelifetime;
        private readonly Dictionary<string, YouAuthAuthorizationCode> _authorizationCodes = new();
        private readonly object _mutex = new();

        //

        public YouAuthAuthorizationCodeManager()
        {
            _authorizationCodelifetime = TimeSpan.FromSeconds(1000); // SEB:TODO from config
        }

        //

        public ValueTask<string> CreateAuthorizationCode(string initiator, string subject)
        {
            if (string.IsNullOrWhiteSpace(initiator))
            {
                throw new YouAuthException("Invalid initiator");
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new YouAuthException("Invalid subject");
            }

            var code = Guid.NewGuid().ToString(); // SEB:TODO use secure?
            var authorizationCode = new YouAuthAuthorizationCode(initiator, subject, code, _authorizationCodelifetime);

            lock (_mutex)
            {
                _authorizationCodes.Add(code, authorizationCode);
            }

            return new ValueTask<string>(code);
        }

        //

        public ValueTask<bool> ValidateAuthorizationCode(string initiator, string authorizationCode)
        {
            if (string.IsNullOrEmpty(authorizationCode) || string.IsNullOrEmpty(initiator))
            {
                return new ValueTask<bool>(false);
            }

            YouAuthAuthorizationCode? ac;
            lock(_mutex)
            {
                _authorizationCodes.Remove(authorizationCode, out ac);
            }

            if (ac == null || ac.HasExpired || ac.Initiator != initiator)
            {
                return new ValueTask<bool>(false);
            }

            return new ValueTask<bool>(true);
        }

        //

        private class YouAuthAuthorizationCode
        {
            public DateTimeOffset CreatedAt { get; }
            public DateTimeOffset ExpiresAt { get; }
            public string Initiator { get; }
            public string Subject { get; }
            public string Value { get; }

            public YouAuthAuthorizationCode(string initiator, string subject, string value, TimeSpan lifetime)
            {
                CreatedAt = DateTimeOffset.Now;
                Initiator = initiator;
                Subject = subject;
                Value = value;
                ExpiresAt = CreatedAt + lifetime;
            }

            public bool HasExpired => DateTimeOffset.Now > ExpiresAt;
        }
    }

    //


}