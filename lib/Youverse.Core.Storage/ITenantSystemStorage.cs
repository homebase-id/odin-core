using System;
using System.Threading.Tasks;
using Youverse.Core.Storage.SQLite.KeyValue;

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

        public ThreeKeyValueStorage IcrClientStorage { get; }

        public TableCircleMember CircleMemberStorage { get; }
    }
}