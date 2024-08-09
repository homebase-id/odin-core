using System.Data.Common;
using System.Threading.Tasks;
using Npgsql;

namespace Odin.Core.Storage.RepositoryPattern.Connection;

public static class NpgsqlConcreteConnectionFactory
{
    public static async Task<DbConnection> Create(string connectionString)
    {
        // SEB:TODO do we need explicit retry logic here?
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}