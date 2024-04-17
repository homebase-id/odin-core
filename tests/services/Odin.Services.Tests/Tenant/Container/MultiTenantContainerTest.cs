using System;
using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Odin.Services.Registry;
using Odin.Services.Tenant;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Tests.Tenant.Container;

public class MultiTenantContainerTest
{
    [Test]
    public void ItShouldLookupTenantScope()
    {
        var mockRegistry = new Mock<IIdentityRegistry>();
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);

        var host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(
                new MultiTenantServiceProviderFactory(
                    (builder, tenant) =>
                    {
                        // Register per-tenant services
                        var scopedInfo = new SomeScopedInfo {Name1 = tenant.Name};
                        builder.RegisterInstance(scopedInfo).As<SomeScopedInfo>().SingleInstance();
                    },
                    (scope, tenant) =>
                    {
                        // Initialize per-tenant services
                        var scopedInfo = scope.Resolve<SomeScopedInfo>();
                        Assert.AreEqual(tenant.Name, scopedInfo.Name1);
                        scopedInfo.Name2 = tenant.Name;
                    }))
            .ConfigureServices((hostContext, services) =>
            {
                // Standard ASP.NET root services
                services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
                services.AddSingleton<IIdentityRegistry>(mockRegistry.Object);
                services.AddSingleton<ITenantProvider>(mockTenantProvider.Object);
                services.AddSingleton(new SomeScopedInfo {Name1 = "global root 1", Name2 = "global root 2"});
            })
            .Build();

        var scopedInfo = host.Services.GetRequiredService<SomeScopedInfo>();
        Assert.AreEqual("global root 1", scopedInfo.Name1);
        Assert.AreEqual("global root 2", scopedInfo.Name2);

        var containerAccessor = host.Services.GetRequiredService<IMultiTenantContainerAccessor>();
        var container = containerAccessor.Container();

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        var scope = container.GetCurrentTenantScope();
        Assert.IsNotNull(scope);
        scopedInfo = scope.Resolve<SomeScopedInfo>();
        Assert.AreEqual("global root 1", scopedInfo.Name1);
        Assert.AreEqual("global root 2", scopedInfo.Name2);

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(null as Services.Tenant.Tenant);
        scope = container.LookupTenantScope("example.com");
        Assert.IsNull(scope);

        mockTenantProvider.Setup(x => x.GetCurrentTenant()).Returns(new Services.Tenant.Tenant("example.com"));
        scope = container.GetCurrentTenantScope();
        Assert.IsNotNull(scope);
        scopedInfo = scope.Resolve<SomeScopedInfo>();
        Assert.AreEqual("example.com", scopedInfo.Name1);
        Assert.AreEqual("example.com", scopedInfo.Name2);

        scope = container.LookupTenantScope("example.com");
        Assert.IsNotNull(scope);
        scopedInfo = scope!.Resolve<SomeScopedInfo>();
        Assert.AreEqual("example.com", scopedInfo.Name1);
        Assert.AreEqual("example.com", scopedInfo.Name2);

        Assert.That(scopedInfo.IsDisposed, Is.False);
        host.Dispose();
        Assert.That(scopedInfo.IsDisposed, Is.True);
    }
}

internal class SomeScopedInfo : IDisposable
{
    public bool IsDisposed { get; private set; }
    public string Name1 { get; set; } = "placeholder1";
    public string Name2 { get; set; } = "placeholder2";

    public void Dispose()
    {
        IsDisposed = true;
    }
}
