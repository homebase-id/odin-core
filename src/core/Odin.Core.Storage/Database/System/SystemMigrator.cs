using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.System.Connection;

namespace Odin.Core.Storage.Database.System;

public partial class SystemMigrator(
    ILogger<SystemMigrator> logger, ScopedSystemConnectionFactory scopedConnectionFactory) :
    AbstractMigrator(logger, scopedConnectionFactory)
{
}

