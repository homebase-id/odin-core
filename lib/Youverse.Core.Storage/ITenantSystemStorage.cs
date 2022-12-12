using System;
using System.Threading.Tasks;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace Youverse.Core.Storage
{
    /// <summary>
    /// Storage of system specific data.
    /// </summary>
    public interface ITenantSystemStorage
    {
        
        ThreeKeyValueStorage ThreeKeyValueStorage { get; }

        SingleKeyValueStorage SingleKeyValueStorage { get; }
        
        TableOutbox Outbox { get; }
        
        TableInbox Inbox { get; }

        KeyValueDatabase GetDBInstance();
    }
}