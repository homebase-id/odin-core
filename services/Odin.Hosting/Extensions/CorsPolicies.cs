using Microsoft.Extensions.DependencyInjection;

namespace Odin.Hosting.Extensions;

public static class CorsPolicies
{
    public const string AllowAllOriginsWithCredentialsPolicy = "AllowAllOriginsWithCredentials";

    public static IServiceCollection AddCorsPolicies(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(AllowAllOriginsWithCredentialsPolicy, builder =>
                builder.SetIsOriginAllowed(_ => true)
                    .AllowCredentials()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
        });

        return services;
    }
}