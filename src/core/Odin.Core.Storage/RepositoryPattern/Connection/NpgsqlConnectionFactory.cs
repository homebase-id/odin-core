using System.Data;
using System.Threading.Tasks;
using Npgsql;

namespace Odin.Core.Storage.RepositoryPattern.Connection;

public static class NpgsqlConnectionFactory
{
    public static async Task<IDbConnection> Create(string connectionString)
    {
        // SEB:TODO do we need explicit retry logic here?
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}