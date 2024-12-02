using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Connection;

public class ScopedIdentityConnectionFactory(
    ILogger<ScopedIdentityConnectionFactory> logger,
    IIdentityDbConnectionFactory connectionFactory,
    CacheHelper cache)
    : ScopedConnectionFactory<IIdentityDbConnectionFactory>(logger, connectionFactory, cache)
{
}
