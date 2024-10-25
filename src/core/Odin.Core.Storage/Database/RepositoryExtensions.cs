using System;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.Database.Connection.System;

namespace Odin.Core.Storage.Database;

public enum DatabaseType
{
    Sqlite,
    PostgreSql
}

public static class RepositoryExtensions
{
    public static void AddRepositories(this IServiceCollection services, DatabaseType databaseType, string connectionString)
    {
        //
        // Common / standard sql stuff
        //
        // services.AddScoped<IJobRepository, JobRepository>();

        //
        // Database specific sql stuff
        //
        if (databaseType == DatabaseType.Sqlite)
        {
            services.AddScoped<ISystemDbConnectionFactory>(_ => new SqliteSystemDbConnectionFactory(connectionString));
            // services.AddScoped<IJobRepositoryStrategy, SqliteJobRepositoryStrategy>();
        }
        else if (databaseType == DatabaseType.PostgreSql)
        {
            services.AddScoped<ISystemDbConnectionFactory>(_ => new PgsqlSystemDbConnectionFactory(connectionString));
            // services.AddScoped<IJobRepositoryStrategy, PostgreSqlJobRepositoryStrategy>();
        }
        else
        {
            throw new Exception("Unsupported database type");
        }

    }
}