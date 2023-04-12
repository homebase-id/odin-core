using System;
using System.Threading.Tasks;
using Youverse.Core.Storage.Sqlite;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;

namespace Youverse.Core.Storage
{
    /// <summary>
    /// Storage of system specific data.
    /// </summary>
    public interface ITenantSystemStorage : IDisposable
    {
        ThreeKeyValueStorage ThreeKeyValueStorage { get; }

        SingleKeyValueStorage SingleKeyValueStorage { get; }

        TableOutbox Outbox { get; }

        TableInbox Inbox { get; }

        TableImFollowing WhoIFollow { get; }

        TableFollowsMe Followers { get; }

        ThreeKeyValueStorage IcrClientStorage { get; }

        TableCircleMember CircleMemberStorage { get; }

        DatabaseBase.LogicCommitUnit CreateCommitUnitOfWork();
        
    }
}