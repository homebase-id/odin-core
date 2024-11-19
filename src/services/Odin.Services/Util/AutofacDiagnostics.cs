using System;
using System.Linq;
using Autofac;
using Autofac.Core;
using Autofac.Core.Activators.Reflection;
using Autofac.Core.Lifetime;
using Microsoft.Extensions.Logging;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Util;

public class AutofacDiagnostics
{
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
                // ProvidedInstanceActivator:
                // This activator is used when a specific instance is provided to the container.
                // e.g. builder.RegisterInstance(myServiceInstance).As<IMyService>();

                // DelegateActivator:
                // This activator is used when a delegate is provided to the container.
                // e.g. builder.Register(c => new MyService()).As<IMyService>();

                logger.LogWarning("MANUAL CHECK REQUIRED: {registration} ({activator}) ",
                    registration.Target.Activator.LimitType, registration.Activator.GetType());

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