using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Core.Lifetime;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Tenant;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Tests.Tenant.Container;

public class MultiTenantContainerTest
{
    [Test]
    public void ItShouldLookupTenantScope()
    {
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new MultiTenantServiceProviderFactory())
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
                services.AddSingleton(new SomeScopedData {Name = "global root 1"});
            })
            .Build();

        var scopedInfo = host.Services.GetRequiredService<SomeScopedData>();
        ClassicAssert.AreEqual("global root 1", scopedInfo.Name);

        var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var scope = container.LookupTenantScope("example.com");
        ClassicAssert.IsNull(scope);

        scope = container.GetOrAddTenantScope("example.com", cb =>
        {
            cb.RegisterInstance(new SomeScopedData {Name = "example.com"}).As<SomeScopedData>().SingleInstance();
        });
        ClassicAssert.IsNotNull(scope);
        scopedInfo = scope!.Resolve<SomeScopedData>();
        ClassicAssert.AreEqual("example.com", scopedInfo.Name);

        scope = container.LookupTenantScope("example.com");
        ClassicAssert.IsNotNull(scope);
        scopedInfo = scope!.Resolve<SomeScopedData>();
        ClassicAssert.AreEqual("example.com", scopedInfo.Name);

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

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new MultiTenantServiceProviderFactory())
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
                services.AddSingleton(new SomeScopedData {Name = "global root 1"});
            })
            .Build();

        const string domain = "example.com";

        var outerContainer = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
        outerContainer.GetOrAddTenantScope(domain, cb =>
        {
            cb.RegisterInstance(new SomeScopedData {Name = domain}).As<SomeScopedData>().SingleInstance();
        });

        await Task.Run(() =>
        {
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
            var scope = container.GetTenantScope(domain);
            ClassicAssert.IsNotNull(scope);
            var scopedInfo = scope.Resolve<SomeScopedData>();
            ClassicAssert.AreEqual(domain, scopedInfo.Name);
            scopedInfo.Name = "foo";
        });

        await Task.Run(() =>
        {
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
            var scope = container.GetTenantScope(domain);
            ClassicAssert.IsNotNull(scope);
            var scopedInfo = scope.Resolve<SomeScopedData>();
            ClassicAssert.AreEqual("foo", scopedInfo.Name);
        });

        {
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
            var scope = container.GetTenantScope(domain);
            ClassicAssert.IsNotNull(scope);
            var scopedInfo = scope.Resolve<SomeScopedData>();
            ClassicAssert.AreEqual("foo", scopedInfo.Name);

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

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new MultiTenantServiceProviderFactory())
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
            })
            .Build();

        const string domain = "example.com";

        var outerContainer = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
        var outerScope = outerContainer.GetOrAddTenantScope(domain, cb =>
        {
            cb.RegisterType<SomeScopedData>().InstancePerLifetimeScope();
        });
        var outerScopedInfo = outerScope.Resolve<SomeScopedData>();
        outerScopedInfo.Name = domain;

        ILifetimeScope? childScope;
        SomeScopedData? scopedInfo;
        await Task.Run(async () =>
        {
            var container = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
            var tenantScope = container.GetTenantScope(domain);
            scopedInfo = tenantScope.Resolve<SomeScopedData>();
            ClassicAssert.AreEqual(domain, scopedInfo.Name);

            childScope = tenantScope.BeginLifetimeScope("some-scope");

            scopedInfo = childScope.Resolve<SomeScopedData>();
            ClassicAssert.AreEqual("placeholder1", scopedInfo.Name);
            scopedInfo.Name = "child";

            scopedInfo = tenantScope.Resolve<SomeScopedData>();
            ClassicAssert.AreEqual(domain, scopedInfo.Name);

            var serviceProvider = new AutofacServiceProvider(childScope); // demonstrate dotnet core DI vs autofac

            await Task.Run(() =>
            {
                // Can use new child scope
                scopedInfo = serviceProvider.GetRequiredService<SomeScopedData>();
                ClassicAssert.AreEqual("child", scopedInfo.Name);

                // Can use new parent scope as well
                scopedInfo = tenantScope.Resolve<SomeScopedData>();
                ClassicAssert.AreEqual(domain, scopedInfo.Name);
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
        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new MultiTenantServiceProviderFactory())
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
                var tenantScopeAutofac = container.GetOrAddTenantScope(tenant, cb =>
                {
                    cb.RegisterType<SomeScopedData>().InstancePerLifetimeScope();
                });

                // Update a value on the tenant-scope using autofac to illustrate that the correct scope is being used
                var scopedInfo = tenantScopeAutofac.Resolve<SomeScopedData>();
                scopedInfo.Name = "foo";

                // Create a service provider for the tenant scope (to illustrate the difference between dotnet core DI and Autofac)
                var tenantScopeDotnet = new AutofacServiceProvider(tenantScopeAutofac);

                // Reload the value from the tenant-scope using dotnet to illustrate that the correct scope is being used
                scopedInfo = tenantScopeDotnet.GetRequiredService<SomeScopedData>();
                ClassicAssert.AreEqual("foo", scopedInfo.Name);

                // Create instance of SomeServiceUser to prove that the correct tenant scope is being used
                // Note that this service is registered as transient (aka InstancePerDependency)
                var someServiceUser = tenantScopeDotnet.GetRequiredService<SomeServiceUser>();
                ClassicAssert.AreEqual("foo", someServiceUser.SomeScopedData.Name);
            });

            // Test that the tenant scope is still the same after the task has run
            var tenantScopeAutofac = container.GetTenantScope(tenant);
            var someServiceUser = tenantScopeAutofac.Resolve<SomeServiceUser>();
            ClassicAssert.AreEqual("foo", someServiceUser.SomeScopedData.Name);
        }
    }

    [Test]
    public void AutofacLifetimeShouldNotDisposeChildren()
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<SomeScopedData>().InstancePerLifetimeScope();

        SomeScopedData outer;
        SomeScopedData inner;

        var container = builder.Build();

        outer = container.Resolve<SomeScopedData>();
        Assert.That(outer.IsDisposed, Is.False);

        var scope = container.BeginLifetimeScope();

        inner = scope.Resolve<SomeScopedData>();
        Assert.That(inner, Is.Not.SameAs(outer));
        Assert.That(inner.IsDisposed, Is.False);

        container.Dispose();
        Assert.That(outer.IsDisposed, Is.True);
        Assert.That(inner.IsDisposed, Is.False);

        scope.Dispose();
        Assert.That(inner.IsDisposed, Is.True);
    }
}

internal class SomeScopedData : IDisposable
{
    public bool IsDisposed { get; private set; }
    public string Name { get; set; } = "placeholder1";

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
