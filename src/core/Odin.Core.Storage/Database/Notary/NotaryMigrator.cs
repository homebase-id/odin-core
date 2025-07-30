using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Notary;

public partial class NotaryMigrator(
    ILogger<NotaryMigrator> logger, ScopedNotaryConnectionFactory scopedConnectionFactory) :
    AbstractMigrator(logger, scopedConnectionFactory)
{
}

