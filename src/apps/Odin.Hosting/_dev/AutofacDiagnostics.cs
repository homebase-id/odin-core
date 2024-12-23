using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autofac;
using Autofac.Core;
using Autofac.Core.Activators.Reflection;
using Autofac.Core.Lifetime;
using Microsoft.Extensions.Logging;
using Odin.Services.Authentication.Transit;
using Odin.Services.DataSubscription;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting._dev;

public class AutofacDiagnostics(IContainer root, ILogger logger)
{
    // The types below (interface and implementations) are verified to not have any (problematic) non-singleton dependencies
    private readonly Dictionary<Type, string> _manualCheckSingletonWhitelist = new()
    {
        {typeof(Odin.Services.Tenant.Container.MultiTenantContainerAccessor), "1da64787"},
        {typeof(Odin.Core.Storage.Database.DatabaseCounters), "e6f1c919"},
        {typeof(Odin.Core.Storage.Database.System.Connection.SqliteSystemDbConnectionFactory), "74c23c98"},
        {typeof(Odin.Core.Storage.Database.System.Connection.PgsqlSystemDbConnectionFactory), "74c23c98"},
        {typeof(Odin.Core.Storage.Database.Identity.Connection.SqliteIdentityDbConnectionFactory), "74c23c98"},
        {typeof(Odin.Core.Storage.Database.Identity.Connection.PgsqlIdentityDbConnectionFactory), "74c23c98"},
        {typeof(Odin.Core.Storage.CacheHelper), "b6b4e9b2"},
        {typeof(Odin.Hosting.Controllers.Registration.RegistrationRestrictedAttribute), "e7045f27"},
        {typeof(Odin.Hosting.Controllers.Admin.AdminApiRestrictedAttribute), "509d6046"},
        {typeof(Odin.Services.Email.IEmailSender), "eacd5b8f"},
        {typeof(Odin.Services.Certificate.ICertesAcme), "c742bc8f"},
        {typeof(Odin.Services.Dns.IDnsRestClient), "c363ae45"},
        {typeof(Odin.Services.Certificate.AcmeAccountConfig), "e6f1c919"},
        {typeof(Odin.Services.Configuration.OdinConfiguration), "6fba0459"},
        {typeof(Odin.Services.Certificate.CertificateCache), "e6f1c919"},
        {typeof(Odin.Core.Logging.Hostname.StickyHostname), "3b8b6d5d"},
        {typeof(Odin.Core.Logging.CorrelationId.CorrelationContext), "5a40f4fa"},
        {typeof(Odin.Services.Certificate.CertificateServiceFactory), "8a3e1c27"},
        {typeof(Odin.Core.Storage.Database.Identity.Abstractions.IdentityKey), "cfcb55a8"},
        {typeof(Odin.Services.Background.BackgroundServiceManager), "0e9af6f6"},
        {typeof(Odin.Services.Drives.DriveCore.Storage.DriveFileReaderWriter), "80cf458d"},
        {typeof(Odin.Services.Registry.IIdentityRegistry), "ee4e251f"},
        {typeof(Odin.Services.Base.SharedOdinContextCache<FollowerAuthenticationService>), "822f02f2"},
        {typeof(Odin.Services.Base.SharedOdinContextCache<IdentitiesIFollowAuthenticationService>), "822f02f2"},
        {typeof(Odin.Services.Base.SharedOdinContextCache<TransitAuthenticationService>), "822f02f2"},
    };

    public void AssertSingletonDependencies()
    {
        var sw = Stopwatch.StartNew();

        // Check root
        CheckSingletonDependencies(root, logger);

        // Check tenant
        var tenantScope = root.Resolve<IMultiTenantContainerAccessor>().GetTenantScopesForDiagnostics().First();
        CheckSingletonDependencies(tenantScope, logger);

        logger.LogDebug("Singleton dependency check took {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
    }
    
    private void CheckSingletonDependencies(ILifetimeScope container, ILogger logger)
    {
        var allRegistrations = container.ComponentRegistry.Registrations;

        // Filter registrations with any singleton lifetime (RootScopeLifetime or MatchingScopeLifetime)
        var singletonRegistrations = allRegistrations.Where(r => r.Lifetime is RootScopeLifetime or MatchingScopeLifetime);

        foreach (var registration in singletonRegistrations)
        {
            var serviceType = registration.Target.Activator.LimitType;
            var serviceNamespace = serviceType.Namespace;

            if (string.IsNullOrEmpty(serviceNamespace) || !serviceNamespace.StartsWith("Odin"))
            {
                continue;
            }

            if (registration.Activator is not ReflectionActivator reflectionActivator)
            {
                // ProvidedInstanceActivator:
                // This activator is used when a specific instance is provided to the container.
                // e.g. builder.RegisterInstance(myServiceInstance).As<IMyService>();

                // DelegateActivator:
                // This activator is used when a delegate is provided to the container.
                // e.g. builder.Register(c => new MyService()).As<IMyService>();
                
                // Autofac.Core.Registration.ExternalComponentRegistration+NoOpActivator:
                // is used internally by Autofac for services registered through the .NET IServiceCollection
                // integration (when integrating Autofac with ASP.NET Core). These registrations are placeholders
                // that indicate the service was registered in the IServiceCollection but does not need additional
                // instantiation or activation logic within Autofac.

                var ctorHash = GetConstructorSignatureHash(serviceType);
                if (_manualCheckSingletonWhitelist.ContainsKey(serviceType) && _manualCheckSingletonWhitelist[serviceType] == ctorHash)
                {
                    continue;
                }

                logger.LogError("MANUAL CHECK AND WHITE LISTING REQUIRED: {serviceType}={ctorhash} ({activator}) ",
                    serviceType, ctorHash, registration.Activator.GetType());

                continue;
            }

            var service = registration.Target.Activator.LimitType;

            // Use the constructor finder to get available constructors
            var constructorFinder = reflectionActivator.ConstructorFinder;
            var constructorInfos = constructorFinder.FindConstructors(reflectionActivator.LimitType).ToList();
            foreach (var constructorInfo in constructorInfos)
            {
                foreach (var parameter in constructorInfo.GetParameters())
                {
                    var parameterType = parameter.ParameterType;

                    var ctorHash = GetConstructorSignatureHash(serviceType);
                    if (_manualCheckSingletonWhitelist.ContainsKey(serviceType) && _manualCheckSingletonWhitelist[serviceType] == ctorHash)
                    {
                        continue;
                    }

                    var parameterRegistrations = container.ComponentRegistry
                        .RegistrationsFor(new TypedService(parameterType))
                        .ToList();

                    if (!parameterRegistrations.Any())
                    {
                        // Parameter type is not registered; handle if necessary
                        continue;
                    }

                    foreach (var parameterRegistration in parameterRegistrations)
                    {
                        var dependencyLifetime = parameterRegistration.Lifetime;
                        if (dependencyLifetime is not RootScopeLifetime && dependencyLifetime is not MatchingScopeLifetime)
                        {
                            // It's either scoped or transient
                            logger.LogError("Singleton service {service}={ctorhash} depends on non-singleton service '{parameter}' with lifetime '{lifetime}'",
                                service, ctorHash, parameterType.FullName, dependencyLifetime.GetType().Name);
                        }
                    }
                }
            }
        }


    }

    //

    private static string GetConstructorSignatureHash(Type type)
    {
        if (type.IsInterface)
        {
            // Iterate over all implementations of the interface
            var implementations = GetImplementationsOfInterface(type);
            var hashes = implementations.Select(GetConstructorSignatureHash);

            // Combine all implementation hashes
            return string.Join(";", hashes);
        }

        // Get all public constructors of the type
        var constructors = type.GetConstructors();

        // Create a string representation of each constructor
        var constructorSignatures = constructors
            .Select(ctor => new
            {
                Name = ctor.Name,
                Parameters = ctor.GetParameters()
                    .Select(param => $"{param.ParameterType.FullName} {param.Name}")
                    .ToArray()
            })
            .Select(ctor =>
                $"{ctor.Name}({string.Join(", ", ctor.Parameters)})"
            )
            .OrderBy(signature => signature); // Ensure consistent ordering

        // Combine all signatures into a single string
        var combinedSignatures = string.Join(";", constructorSignatures);

        // Hash the combined string
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combinedSignatures));

        // Convert hash to a readable string
        return BitConverter.ToString(hashBytes, 0, 8 / 2).Replace("-", "").ToLower();
    }

    private static IEnumerable<Type> GetImplementationsOfInterface(Type interfaceType)
    {
        // Check all loaded assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        return assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(t => interfaceType.IsAssignableFrom(t) // Check if the type implements the interface
                        && !t.IsAbstract                // Exclude abstract classes
                        && !t.IsInterface);             // Exclude the interface itself
    }
}
