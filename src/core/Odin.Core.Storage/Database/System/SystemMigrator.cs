using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database.System.Connection;

namespace Odin.Core.Storage.Database.System;

public partial class SystemMigrator(
    ILogger<SystemMigrator> logger,
    ScopedSystemConnectionFactory scopedConnectionFactory,
    INodeLock nodeLock) :
    AbstractMigrator(logger, scopedConnectionFactory, nodeLock)
{
}
