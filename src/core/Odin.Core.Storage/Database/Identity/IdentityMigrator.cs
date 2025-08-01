using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity;

public partial class IdentityMigrator(
    ILogger<IdentityMigrator> logger,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    INodeLock nodeLock) :
    AbstractMigrator(logger, scopedConnectionFactory, nodeLock)
{
}