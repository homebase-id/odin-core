using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Notarius.Database.Connection;

namespace Odin.Notarius.Database;

public partial class NotaryMigrator(
    ILogger<NotaryMigrator> logger,
    ScopedNotaryConnectionFactory scopedConnectionFactory,
    INodeLock nodeLock) :
    AbstractMigrator(logger, scopedConnectionFactory, nodeLock)
{
}

