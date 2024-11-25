using System;
using System.Collections.Generic;
using System.Linq;
using Autofac;
using Autofac.Core;
using Autofac.Core.Activators.Reflection;
using Autofac.Core.Lifetime;
using Microsoft.Extensions.Logging;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting._dev;

public static class AutofacDiagnostics
{
    // The types below (interface and implementations) are verified to not have any (problematic) non-singleton dependencies
    private static readonly HashSet<Type> ManualCheckSingletonWhitelist = [
        typeof(Odin.Services.Tenant.Container.MultiTenantContainerAccessor),
        typeof(Odin.Core.Storage.Database.System.Connection.SqliteSystemDbConnectionFactory),
        typeof(Odin.Core.Storage.Database.System.Connection.PgsqlSystemDbConnectionFactory),
        typeof(Odin.Core.Storage.Database.Identity.Connection.SqliteIdentityDbConnectionFactory),
        typeof(Odin.Core.Storage.Database.Identity.Connection.PgsqlIdentityDbConnectionFactory),
        typeof(Odin.Core.Storage.CacheHelper),
        typeof(Odin.Hosting.Controllers.Registration.RegistrationRestrictedAttribute),
        typeof(Odin.Hosting.Controllers.Admin.AdminApiRestrictedAttribute),
        typeof(Odin.Services.Email.IEmailSender),
        typeof(Odin.Services.Certificate.ICertesAcme),
        typeof(Odin.Services.Dns.IDnsRestClient),
        typeof(Odin.Services.Certificate.AcmeAccountConfig),
        typeof(Odin.Services.Configuration.OdinConfiguration),
        typeof(Odin.Services.Certificate.CertificateCache),
        typeof(Odin.Core.Logging.Hostname.StickyHostname),
        typeof(Odin.Core.Logging.CorrelationId.CorrelationContext),
        typeof(Odin.Services.Certificate.CertificateServiceFactory),
        typeof(Odin.Core.Storage.Database.Identity.Abstractions.IdentityKey),
        typeof(Odin.Services.Background.BackgroundServiceManager),
        typeof(Odin.Services.Drives.DriveCore.Storage.DriveFileReaderWriter),
        
    ]; 

    public static void AssertSingletonDependencies(IContainer root, ILogger logger)
    {
#if DEBUG
        // Check root
        CheckSingletonDependencies(root, logger);

        // Check tenant
        var tenantScope = root.Resolve<IMultiTenantContainerAccessor>().GetTenantScopesForDiagnostics().First();
        CheckSingletonDependencies(tenantScope, logger);
#endif
    }
    
    private static void CheckSingletonDependencies(ILifetimeScope container, ILogger logger)
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
                if (ManualCheckSingletonWhitelist.Contains(serviceType))
                {
                    continue;
                }
                
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

                logger.LogError("MANUAL CHECK AND WHITE LISTING REQUIRED: {serviceType} ({activator}) ",
                    serviceType, registration.Activator.GetType());

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

                    if (parameterType == typeof(ILifetimeScope))
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
                            logger.LogError("Singleton service '{service}' depends on non-singleton service '{parameter}' with lifetime '{lifetime}'",
                                service, parameterType.FullName, dependencyLifetime.GetType().Name);
                        }
                    }
                }
            }
        }
    }
}