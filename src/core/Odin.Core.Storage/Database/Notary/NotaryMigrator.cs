using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database.Notary.Connection;

namespace Odin.Core.Storage.Database.Notary;

public partial class NotaryMigrator(
    ILogger<NotaryMigrator> logger,
    ScopedNotaryConnectionFactory scopedConnectionFactory,
    INodeLock nodeLock) :
    AbstractMigrator(logger, scopedConnectionFactory, nodeLock)
{
}

