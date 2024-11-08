using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Odin.Core.Tasks;
using Odin.Services.Configuration;
using Odin.Services.Tenant;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Tests.Tenant.Container;

public class MultiTenantContainerTest
{
    [Test]
    public void ItShouldCreateScopeForCurrentTenant()
    {
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var configMock = new OdinConfiguration();

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(
                new MultiTenantServiceProviderFactory(
                    (builder, tenant, config) =>
                    {
                        // Register per-tenant services
                        var scopedInfo = new SomeScopedData {Name1 = tenant.Name};
                        builder.RegisterInstance(scopedInfo).As<SomeScopedData>().SingleInstance();
                    },
                    (scope, tenant) =>
                    {
                        // Initialize per-tenant services
                        var scopedInfo = scope.Resolve<SomeScopedData>();
                        Assert.AreEqual(tenant.Name, scopedInfo.Name1);
                        scopedInfo.Name2 = tenant.Name;
                    },
                    configMock))
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
                services.AddSingleton(new SomeScopedData {Name1 = "global root 1", Name2 = "global root 2"});
            })
            .Build();

        var scopedInfo = host.Services.GetRequiredService<SomeScopedData>();
        Assert.AreEqual("global root 1", scopedInfo.Name1);
        Assert.AreEqual("global root 2", scopedInfo.Name2);

        var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var scope = container.GetCurrentTenantScope();
        Assert.IsNotNull(scope);
        scopedInfo = scope.Resolve<SomeScopedData>();
        Assert.AreEqual("global root 1", scopedInfo.Name1);
        Assert.AreEqual("global root 2", scopedInfo.Name2);

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(new Services.Tenant.Tenant("example.com"));
        scope = container.GetCurrentTenantScope();
        Assert.IsNotNull(scope);
        scopedInfo = scope.Resolve<SomeScopedData>();
        Assert.AreEqual("example.com", scopedInfo.Name1);
        Assert.AreEqual("example.com", scopedInfo.Name2);

        Assert.That(scopedInfo.IsDisposed, Is.False);
        host.Dispose();
        Assert.That(scopedInfo.IsDisposed, Is.True);
    }

    [Test]
    public void ItShouldLookupTenantScope()
    {
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var configMock = new OdinConfiguration();

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(
                new MultiTenantServiceProviderFactory(
                    (builder, tenant, config) =>
                    {
                        // Register per-tenant services
                        var scopedInfo = new SomeScopedData {Name1 = tenant.Name};
                        builder.RegisterInstance(scopedInfo).As<SomeScopedData>().SingleInstance();
                    },
                    (scope, tenant) =>
                    {
                        // Initialize per-tenant services
                        var scopedInfo = scope.Resolve<SomeScopedData>();
                        Assert.AreEqual(tenant.Name, scopedInfo.Name1);
                        scopedInfo.Name2 = tenant.Name;
                    },
                    configMock))
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
                services.AddSingleton(new SomeScopedData {Name1 = "global root 1", Name2 = "global root 2"});
            })
            .Build();

        var scopedInfo = host.Services.GetRequiredService<SomeScopedData>();
        Assert.AreEqual("global root 1", scopedInfo.Name1);
        Assert.AreEqual("global root 2", scopedInfo.Name2);

        var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var scope = container.GetCurrentTenantScope();
        Assert.IsNotNull(scope);
        scopedInfo = scope.Resolve<SomeScopedData>();
        Assert.AreEqual("global root 1", scopedInfo.Name1);
        Assert.AreEqual("global root 2", scopedInfo.Name2);

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        scope = container.LookupTenantScope("example.com");
        Assert.IsNull(scope);

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(new Services.Tenant.Tenant("example.com"));
        scope = container.GetCurrentTenantScope();
        Assert.IsNotNull(scope);
        scopedInfo = scope.Resolve<SomeScopedData>();
        Assert.AreEqual("example.com", scopedInfo.Name1);
        Assert.AreEqual("example.com", scopedInfo.Name2);

        scope = container.LookupTenantScope("example.com");
        Assert.IsNotNull(scope);
        scopedInfo = scope!.Resolve<SomeScopedData>();
        Assert.AreEqual("example.com", scopedInfo.Name1);
        Assert.AreEqual("example.com", scopedInfo.Name2);

        Assert.That(scopedInfo.IsDisposed, Is.False);
        host.Dispose();
        Assert.That(scopedInfo.IsDisposed, Is.True);
    }

    //

    [Test]
    public void ItShouldGetOrCreateNamedTenantScope()
    {
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var configMock = new OdinConfiguration();

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(
                new MultiTenantServiceProviderFactory(
                    (builder, tenant, config) =>
                    {
                        // Register per-tenant services
                        var scopedInfo = new SomeScopedData {Name1 = tenant.Name};
                        builder.RegisterInstance(scopedInfo).As<SomeScopedData>().SingleInstance();
                    },
                    (scope, tenant) =>
                    {
                        // Initialize per-tenant services
                        var scopedInfo = scope.Resolve<SomeScopedData>();
                        Assert.AreEqual(tenant.Name, scopedInfo.Name1);
                        scopedInfo.Name2 = tenant.Name;
                    },
                    configMock))
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
                services.AddSingleton(new SomeScopedData {Name1 = "global root 1", Name2 = "global root 2"});
            })
            .Build();

        var scopedInfo = host.Services.GetRequiredService<SomeScopedData>();
        Assert.AreEqual("global root 1", scopedInfo.Name1);
        Assert.AreEqual("global root 2", scopedInfo.Name2);

        var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();

        const string domain = "example.com";
        var scope = container.GetTenantScope(domain);
        Assert.IsNotNull(scope);
        scopedInfo = scope.Resolve<SomeScopedData>();
        Assert.AreEqual(domain, scopedInfo.Name1);
        Assert.AreEqual(domain, scopedInfo.Name2);

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        scope = container.LookupTenantScope(domain);
        Assert.IsNotNull(scope);
        scopedInfo = scope!.Resolve<SomeScopedData>();
        Assert.AreEqual(domain, scopedInfo.Name1);
        Assert.AreEqual(domain, scopedInfo.Name2);

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(new Services.Tenant.Tenant(domain));
        scope = container.GetCurrentTenantScope();
        Assert.IsNotNull(scope);
        scopedInfo = scope.Resolve<SomeScopedData>();
        Assert.AreEqual(domain, scopedInfo.Name1);
        Assert.AreEqual(domain, scopedInfo.Name2);

        Assert.That(scopedInfo.IsDisposed, Is.False);
        host.Dispose();
        Assert.That(scopedInfo.IsDisposed, Is.True);
    }

    //

    [Test]
    public async Task ItShouldAccessScopeAcrossTasks()
    {
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var configMock = new OdinConfiguration();

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(
                new MultiTenantServiceProviderFactory(
                    (builder, tenant, config) =>
                    {
                        // Register per-tenant services
                        var scopedInfo = new SomeScopedData {Name1 = tenant.Name};
                        builder.RegisterInstance(scopedInfo).As<SomeScopedData>().SingleInstance();
                    },
                    (scope, tenant) =>
                    {
                        // Initialize per-tenant services
                        var scopedInfo = scope.Resolve<SomeScopedData>();
                        Assert.AreEqual(tenant.Name, scopedInfo.Name1);
                        scopedInfo.Name2 = tenant.Name;
                    },
                    configMock))
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
                services.AddSingleton(new SomeScopedData {Name1 = "global root 1", Name2 = "global root 2"});
            })
            .Build();

        const string domain = "example.com";
        await Task.Run(() =>
        {
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
            var scope = container.GetTenantScope(domain);
            Assert.IsNotNull(scope);
            var scopedInfo = scope.Resolve<SomeScopedData>();
            Assert.AreEqual(domain, scopedInfo.Name1);
            Assert.AreEqual(domain, scopedInfo.Name2);

            scopedInfo.Name1 = "foo";
            scopedInfo.Name2 = "bar";
        });

        await Task.Run(() =>
        {
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
            var scope = container.GetTenantScope(domain);
            Assert.IsNotNull(scope);
            var scopedInfo = scope.Resolve<SomeScopedData>();
            Assert.AreEqual("foo", scopedInfo.Name1);
            Assert.AreEqual("bar", scopedInfo.Name2);
        });

        {
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
            var scope = container.GetTenantScope(domain);
            Assert.IsNotNull(scope);
            var scopedInfo = scope.Resolve<SomeScopedData>();
            Assert.AreEqual("foo", scopedInfo.Name1);
            Assert.AreEqual("bar", scopedInfo.Name2);

            Assert.That(scopedInfo.IsDisposed, Is.False);
            host.Dispose();
            Assert.That(scopedInfo.IsDisposed, Is.True);
        }
    }

    //

    [Test]
    public async Task ItShouldIsolateChildScopesAcrossTasks()
    {
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var configMock = new OdinConfiguration();

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(
                new MultiTenantServiceProviderFactory(
                    (builder, tenant, config) =>
                    {
                        // Register per-tenant services
                        builder.RegisterType<SomeScopedData>().AsSelf().InstancePerLifetimeScope();
                    },
                    (scope, tenant) =>
                    {
                        // Initialize per-tenant services
                        var scopedInfo = scope.Resolve<SomeScopedData>();
                        scopedInfo.Name1 = scopedInfo.Name2 = tenant.Name;
                    },
                    configMock))
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
            })
            .Build();

        const string domain = "example.com";
        ILifetimeScope? childScope;
        SomeScopedData? scopedInfo;
        await Task.Run(async () =>
        {
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
            var tenantScope = container.GetTenantScope(domain);
            scopedInfo = tenantScope.Resolve<SomeScopedData>();
            Assert.AreEqual(domain, scopedInfo.Name1);
            Assert.AreEqual(domain, scopedInfo.Name2);

            childScope = tenantScope.BeginLifetimeScope("some-scope");

            scopedInfo = childScope.Resolve<SomeScopedData>();
            Assert.AreEqual("placeholder1", scopedInfo.Name1);
            Assert.AreEqual("placeholder2", scopedInfo.Name2);
            scopedInfo.Name1 = scopedInfo.Name2 = "child";

            scopedInfo = tenantScope.Resolve<SomeScopedData>();
            Assert.AreEqual(domain, scopedInfo.Name1);
            Assert.AreEqual(domain, scopedInfo.Name2);

            var serviceProvider = new AutofacServiceProvider(childScope); // demonstrate dotnet core DI vs autofac

            await Task.Run(() =>
            {
                // Can use new child scope
                scopedInfo = serviceProvider.GetRequiredService<SomeScopedData>();
                Assert.AreEqual("child", scopedInfo.Name1);
                Assert.AreEqual("child", scopedInfo.Name2);

                // Can use new parent scope as well
                scopedInfo = tenantScope.Resolve<SomeScopedData>();
                Assert.AreEqual(domain, scopedInfo.Name1);
                Assert.AreEqual(domain, scopedInfo.Name2);
            });

            scopedInfo = childScope.Resolve<SomeScopedData>();
            Assert.That(scopedInfo.IsDisposed, Is.False);
            childScope.Dispose();
            Assert.That(scopedInfo.IsDisposed, Is.True);

        });
    }

    //

    [Test]
    public async Task ItShouldUseCorrectTenantScopeInChildTasks()
    {
        var configMock = new OdinConfiguration();

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(
                new MultiTenantServiceProviderFactory(
                    (builder, tenant, config) =>
                    {
                        // Register per-tenant services
                        var scopedInfo = new SomeScopedData {Name1 = tenant.Name};
                        builder.RegisterInstance(scopedInfo).As<SomeScopedData>().SingleInstance();
                    },
                    (scope, tenant) =>
                    {
                        // Initialize per-tenant services
                        var scopedInfo = scope.Resolve<SomeScopedData>();
                        Assert.AreEqual(tenant.Name, scopedInfo.Name1);
                        scopedInfo.Name2 = tenant.Name;
                    },
                    configMock))
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services here
                services.AddTransient<SomeServiceUser>();
            })
            .Build();

        await DemoTenantScopeUsedInTask("frodo.doyou.cloud");
        return;

        async Task DemoTenantScopeUsedInTask(string tenant)
        {
            // Simulate injected IMultiTenantContainerAccessor
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();

            await Task.Run(() =>
            {
                // Get (or, in this particular case: create) scope for tenant
                var tenantScopeAutofac = container.GetTenantScope(tenant);

                // Update a value on the tenant-scope using autofac to illustrate that the correct scope is being used
                var scopedInfo = tenantScopeAutofac.Resolve<SomeScopedData>();
                scopedInfo.Name1 = "foo";
                scopedInfo.Name2 = "bar";

                // Create a service provider for the tenant scope (to illustrate the difference between dotnet core DI and Autofac)
                var tenantScopeDotnet = new AutofacServiceProvider(tenantScopeAutofac);

                // Reload the value from the tenant-scope using dotnet to illustrate that the correct scope is being used
                scopedInfo = tenantScopeDotnet.GetRequiredService<SomeScopedData>();
                Assert.AreEqual("foo", scopedInfo.Name1);
                Assert.AreEqual("bar", scopedInfo.Name2);

                // Create instance of SomeServiceUser to prove that the correct tenant scope is being used
                // Note that this service is registered as transient (aka InstancePerDependency)
                var someServiceUser = tenantScopeDotnet.GetRequiredService<SomeServiceUser>();
                Assert.AreEqual("foo", someServiceUser.SomeScopedData.Name1);
                Assert.AreEqual("bar", someServiceUser.SomeScopedData.Name2);
            });

            // Test that the tenant scope is still the same after the task has run
            var tenantScopeAutofac = container.GetTenantScope(tenant);
            var someServiceUser = tenantScopeAutofac.Resolve<SomeServiceUser>();
            Assert.AreEqual("foo", someServiceUser.SomeScopedData.Name1);
            Assert.AreEqual("bar", someServiceUser.SomeScopedData.Name2);
        }
    }
}

internal class SomeScopedData : IDisposable
{
    public bool IsDisposed { get; private set; }
    public string Name1 { get; set; } = "placeholder1";
    public string Name2 { get; set; } = "placeholder2";

    public void Dispose()
    {
        IsDisposed = true;
    }
}

internal class SomeServiceUser(SomeScopedData someScopedData, IServiceProvider serviceProvider)
{
    public SomeScopedData SomeScopedData => someScopedData;
    public IServiceProvider ServiceProvider => serviceProvider;
}
