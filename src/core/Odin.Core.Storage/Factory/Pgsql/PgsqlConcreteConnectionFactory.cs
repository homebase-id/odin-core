using System.Data.Common;
using System.Threading.Tasks;
using Npgsql;

namespace Odin.Core.Storage.Factory.Pgsql;

internal static class PgsqlConcreteConnectionFactory
{
    internal static async Task<DbConnection> CreateAsync(string connectionString)
    {
        // SEB:TODO do we need explicit retry logic here?
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
