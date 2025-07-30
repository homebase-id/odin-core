using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity;

public partial class IdentityMigrator(
    ILogger<IdentityMigrator> logger, ScopedIdentityConnectionFactory scopedConnectionFactory) :
    AbstractMigrator(logger, scopedConnectionFactory)
{
}