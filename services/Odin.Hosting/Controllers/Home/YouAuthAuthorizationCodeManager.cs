#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Base;

namespace Odin.Hosting.Controllers.Home
{
    public interface IYouAuthAuthorizationCodeManager
    {
        ValueTask<string> CreateAuthorizationCode(string initiator, string subject);
        ValueTask<bool> ValidateAuthorizationCode(string initiator, string authorizationCode, out SensitiveByteArray? icrKey);
    }

    public sealed class YouAuthAuthorizationCodeManager : IYouAuthAuthorizationCodeManager
    {
        private readonly TimeSpan _authorizationCodeLifeTime;
        private readonly Dictionary<string, YouAuthAuthorizationCode> _authorizationCodes = new();
        private readonly object _mutex = new();
        private readonly OdinContextAccessor _contextAccessor;


        //

        public YouAuthAuthorizationCodeManager(OdinContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
            _authorizationCodeLifeTime = TimeSpan.FromSeconds(1000); // SEB:TODO from config
        }

        //

        public ValueTask<string> CreateAuthorizationCode(string initiator, string subject)
        {
            if (string.IsNullOrWhiteSpace(initiator))
            {
                throw new YouAuthClientException("Invalid initiator");
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new YouAuthClientException("Invalid subject");
            }

            var codeId = Guid.NewGuid(); // SEB:TODO use secure?

            // var codeAsKey = codeId.ToByteArray().ToSensitiveByteArray();

            var code = codeId.ToString();
            var codeAsKey = ByteArrayUtil.ReduceSHA256Hash(code).ToByteArray().ToSensitiveByteArray();

            var icrKey = _contextAccessor.GetCurrent().PermissionsContext.GetIcrKey();
            var encryptedIcrKey = new SymmetricKeyEncryptedAes(ref codeAsKey, ref icrKey);

            var authorizationCode = new YouAuthAuthorizationCode(initiator, subject, code, _authorizationCodeLifeTime, encryptedIcrKey);

            lock (_mutex)
            {
                _authorizationCodes.Add(code, authorizationCode);
            }

            return new ValueTask<string>(code);
        }

        //

        public ValueTask<bool> ValidateAuthorizationCode(string initiator, string authorizationCode, out SensitiveByteArray? icrKey)
        {
            if (string.IsNullOrEmpty(authorizationCode) || string.IsNullOrEmpty(initiator))
            {
                icrKey = null;
                return new ValueTask<bool>(false);
            }

            YouAuthAuthorizationCode? ac;
            lock (_mutex)
            {
                _authorizationCodes.Remove(authorizationCode, out ac);
            }

            if (ac == null || ac.HasExpired || ac.Initiator != initiator)
            {
                icrKey = null;
                return new ValueTask<bool>(false);
            }

            var key = ByteArrayUtil.ReduceSHA256Hash(authorizationCode).ToByteArray().ToSensitiveByteArray();
            icrKey = ac.EncryptedEncryptedIcrKey.DecryptKeyClone(ref key);

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
            public SymmetricKeyEncryptedAes EncryptedEncryptedIcrKey { get; }

            public YouAuthAuthorizationCode(string initiator, string subject, string value, TimeSpan lifetime, SymmetricKeyEncryptedAes encryptedIcrKey)
            {
                CreatedAt = DateTimeOffset.Now;
                Initiator = initiator;
                Subject = subject;
                Value = value;
                ExpiresAt = CreatedAt + lifetime;
                EncryptedEncryptedIcrKey = encryptedIcrKey;
            }

            public bool HasExpired => DateTimeOffset.Now > ExpiresAt;
        }
    }

    //
}