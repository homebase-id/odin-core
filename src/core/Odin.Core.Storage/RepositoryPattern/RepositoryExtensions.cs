using System;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.RepositoryPattern.Connection.System;
using Odin.Core.Storage.RepositoryPattern.Repositories.System;

namespace Odin.Core.Storage.RepositoryPattern;

public enum DatabaseType
{
    Sqlite,
    PostgreSql
}

public static class RepositoryExtensions
{
    public static void AddRepositories(this IServiceCollection services, DatabaseType databaseType)
    {
        const string connectionString = "foobar;todo";

        //
        // Common / standard sql stuff
        //
        services.AddScoped<IJobRepository, JobRepository>();

        //
        // Database specific sql stuff
        //
        if (databaseType == DatabaseType.Sqlite)
        {
            services.AddScoped<ISystemDbConnectionFactory>(_ => new SqliteSystemDbConnectionFactory(connectionString));
            services.AddScoped<IJobRepositoryStrategy, SqliteJobRepositoryStrategy>();
        }
        else if (databaseType == DatabaseType.PostgreSql)
        {
            services.AddScoped<ISystemDbConnectionFactory>(_ => new NpgsqlSystemDbConnectionFactory(connectionString));
            services.AddScoped<IJobRepositoryStrategy, PostgreSqlJobRepositoryStrategy>();
        }
        else
        {
            throw new Exception("Unsupported database type");
        }

    }
}