using System;
using System.Threading.Tasks;
using Youverse.Core.Storage.SQLite;
using Youverse.Core.Storage.SQLite.IdentityDatabase;

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

        void CommitOutstandingTransactions();
    }
}