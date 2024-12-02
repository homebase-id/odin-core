using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.System.Connection;

public class ScopedSystemConnectionFactory(
    ILogger<ScopedSystemConnectionFactory> logger,
    ISystemDbConnectionFactory connectionFactory,
    CacheHelper cache)
    : ScopedConnectionFactory<ISystemDbConnectionFactory>(logger, connectionFactory, cache)
{
}
