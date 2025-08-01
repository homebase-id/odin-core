using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database.KeyChain.Connection;

namespace Odin.Core.Storage.Database.KeyChain;

public partial class KeyChainMigrator(
    ILogger<KeyChainMigrator> logger,
    ScopedKeyChainConnectionFactory scopedConnectionFactory,
    INodeLock nodeLock) :
    AbstractMigrator(logger, scopedConnectionFactory, nodeLock)
{
}