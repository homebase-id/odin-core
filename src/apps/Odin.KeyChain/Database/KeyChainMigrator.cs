using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.KeyChain.Database.Connection;

namespace Odin.KeyChain.Database;

public partial class KeyChainMigrator(
    ILogger<KeyChainMigrator> logger,
    ScopedKeyChainConnectionFactory scopedConnectionFactory,
    INodeLock nodeLock) :
    AbstractMigrator(logger, scopedConnectionFactory, nodeLock)
{
}