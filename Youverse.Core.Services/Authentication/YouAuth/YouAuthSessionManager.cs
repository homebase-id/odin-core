using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthSessionManager : IYouAuthSessionManager
    {
        private readonly TimeSpan _sessionlifetime;
        private readonly Dictionary<Guid, YouAuthSession> _sessionBySessionId = new();
        private readonly Dictionary<string, YouAuthSession> _sessionBySubject = new();
        private readonly ILogger<YouAuthSessionManager> _logger;
        private readonly ReaderWriterLockSlim _mutex = new();

        public YouAuthSessionManager(ILogger<YouAuthSessionManager> logger)
        {
            _sessionlifetime = TimeSpan.FromDays(7); // SEB:TODO read config
            _logger = logger;
        }

        //

        public async ValueTask<YouAuthSession> CreateSession(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new YouAuthException("Invalid subject");
            }

            var sessionId = Guid.NewGuid();
            var session = new YouAuthSession(sessionId, subject, _sessionlifetime);

            _mutex.EnterWriteLock();
            try
            {
                // Delete existing session, if any
                await InternalDeleteFromSubject(subject);

                _sessionBySessionId[sessionId] = session;
                _sessionBySubject[subject] = session;

                // SEB:TODO storage
            }
            finally
            {
                _mutex.ExitWriteLock();
            }

            return session;
        }

        //

        public async ValueTask<YouAuthSession?> LoadFromId(Guid id)
        {
            YouAuthSession? session;

            _mutex.EnterReadLock();
            try
            {
                _sessionBySessionId.TryGetValue(id, out session);
            }
            finally
            {
                _mutex.ExitReadLock();
            }

            if (session == null || session.HasExpired)
            {
                await DeleteFromId(id);
                session = null;
            }

            return session;
        }

        //

        public async ValueTask<YouAuthSession?> LoadFromSubject(string subject)
        {
            YouAuthSession? session;

            _mutex.EnterReadLock();
            try
            {
                _sessionBySubject.TryGetValue(subject, out session);
            }
            finally
            {
                _mutex.ExitReadLock();
            }

            if (session == null || session.HasExpired)
            {
                await DeleteFromSubject(subject);
                session = null;
            }

            return session;
        }

        //

        public ValueTask DeleteFromId(Guid id)
        {
            _mutex.EnterWriteLock();
            try
            {
                return InternalDeleteFromId(id);
            }
            finally
            {
                _mutex.ExitWriteLock();
            }
        }

        //

        private ValueTask InternalDeleteFromId(Guid id)
        {
            if (!_mutex.IsWriteLockHeld)
            {
                throw new YouAuthException("Must call this while mutex has write lock!");
            }

            if (_sessionBySessionId.Remove(id, out YouAuthSession? session))
            {
                _sessionBySubject.Remove(session.Subject);
                // SEB:TODO storage
            }

            return new ValueTask();
        }


        //

        public ValueTask DeleteFromSubject(string subject)
        {
            _mutex.EnterWriteLock();
            try
            {
                return InternalDeleteFromSubject(subject);
            }
            finally
            {
                _mutex.ExitWriteLock();
            }
        }

        //

        private ValueTask InternalDeleteFromSubject(string subject)
        {
            if (!_mutex.IsWriteLockHeld)
            {
                throw new YouAuthException("Must call this while mutex has write lock!");
            }

            if (_sessionBySubject.Remove(subject, out YouAuthSession? session))
            {
                _sessionBySessionId.Remove(session.Id);
                // SEB:TODO storage
            }

            return new ValueTask();
        }

        //

        public ValueTask DeleteExpired()
        {
            List<YouAuthSession> expiredSessions;

            _mutex.EnterReadLock();
            try
            {
                expiredSessions = _sessionBySubject.Values.Where(session => session.HasExpired).ToList();
            }
            finally
            {
                _mutex.ExitReadLock();
            }

            if (expiredSessions.Count > 0)
            {
                _mutex.EnterWriteLock();
                try
                {
                    foreach (var session in expiredSessions)
                    {
                        _sessionBySessionId.Remove(session.Id);
                        _sessionBySubject.Remove(session.Subject);
                    }
                }
                finally
                {
                    _mutex.ExitWriteLock();
                }

                _logger.LogDebug("Deleted {ExpiredSessionCount} expired sessions", expiredSessions.Count);
            }

            return new ValueTask();
        }
    }
}
