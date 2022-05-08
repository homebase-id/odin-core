using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthSessionManager : IYouAuthSessionManager
    {
        private readonly TimeSpan _sessionlifetime;
        private readonly ILogger<YouAuthSessionManager> _logger;
        private readonly IYouAuthSessionStorage _youAuthSessionStorage;
        private readonly object _mutex = new();

        public YouAuthSessionManager(ILogger<YouAuthSessionManager> logger, IYouAuthSessionStorage youAuthSessionStorage)
        {
            _sessionlifetime = TimeSpan.FromDays(7); // SEB:TODO read config
            _logger = logger;
            _youAuthSessionStorage = youAuthSessionStorage;
        }

        //

        [Obsolete("replaced with exchange grant service")]
        public ValueTask<YouAuthSession> CreateSession(string subject, Guid? accessRegistrationId)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new YouAuthException("Invalid subject");
            }

            // NOTE: this lock only works because litedb isn't async
            lock(_mutex)
            {
                var session = _youAuthSessionStorage.LoadFromSubject(subject);
                if (session != null && !session.HasExpired())
                {
                    return new ValueTask<YouAuthSession>(session);
                }

                if (session != null)
                {
                    _youAuthSessionStorage.Delete(session);
                }

                var sessionId = Guid.NewGuid();
                session = new YouAuthSession(sessionId, subject, _sessionlifetime, accessRegistrationId);
                _youAuthSessionStorage.Save(session);

                return new ValueTask<YouAuthSession>(session);
            }
        }

        //

        public ValueTask<YouAuthSession?> LoadFromId(Guid id)
        {
            var session = _youAuthSessionStorage.LoadFromId(id);

            if (session != null && session.HasExpired())
            {
                _youAuthSessionStorage.Delete(session);
                session = null;
            }

            return new ValueTask<YouAuthSession?>(session);
        }

        //

        public ValueTask<YouAuthSession?> LoadFromSubject(string subject)
        {
            var session = _youAuthSessionStorage.LoadFromSubject(subject);

            if (session != null && session.HasExpired())
            {
                _youAuthSessionStorage.Delete(session);
                session = null;
            }

            return new ValueTask<YouAuthSession?>(session);
        }

        //

        public ValueTask DeleteFromSubject(string subject)
        {
            var session = _youAuthSessionStorage.LoadFromSubject(subject);
            if (session != null)
            {
                _youAuthSessionStorage.Delete(session);
            }

            return new ValueTask();
        }

        //

    }
}
