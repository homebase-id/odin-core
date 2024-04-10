using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Services.Base;

namespace Odin.Services.Tests.Base;

public class OdinContextAccessorTest
{
    [Test]
    public void ItShouldReturnOdinContextFromHttpRequest()
    {
        // Arrange
        var builder = new ContainerBuilder();

        builder.Register(c => new OdinContextRootContainer(c.Resolve<ILifetimeScope>())).SingleInstance();
        builder.RegisterType<OdinContext>().AsSelf().InstancePerLifetimeScope();

        var container = builder.Build();
        var serviceProvider = new AutofacServiceProvider(container);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        var odinContext = httpContext.RequestServices.GetRequiredService<OdinContext>();
        odinContext.Tenant = new OdinId("frodo.baggins.mock");

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        httpContextAccessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        var odinContextAccessor = new OdinContextAccessor(
            httpContextAccessorMock.Object,
            serviceProvider.GetRequiredService<OdinContextRootContainer>());

        // Act
        var result = odinContextAccessor.GetCurrent();

        // Assert
        Assert.NotNull(result);
        Assert.AreEqual("frodo.baggins.mock", result.Tenant.DomainName);
    }

    //

    [Test]
    public async Task ItShouldReturnOdinContextFromCustomScope()
    {
        // Arrange
        var builder = new ContainerBuilder();

        builder.Register(c => new OdinContextRootContainer(c.Resolve<ILifetimeScope>())).SingleInstance();
        builder.RegisterType<OdinContextAccessor>().AsSelf().SingleInstance();
        builder.RegisterType<OdinContext>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<SomeOdinContextConsumer>().AsSelf().InstancePerDependency();
        builder.RegisterType<HttpContextAccessor>().As<IHttpContextAccessor>().SingleInstance();

        var container = builder.Build();
        var serviceProvider = new AutofacServiceProvider(container);

        var odinContextAccessor = serviceProvider.GetRequiredService<OdinContextAccessor>();
        var odinContext = new OdinContext { Tenant = new OdinId("frodo.baggins.mock") };

        // Act

        var tenant = "";
        await odinContextAccessor.ExecuteInScope(odinContext, async () =>
        {
            await Task.Delay(10);
            var consumer = serviceProvider.GetRequiredService<SomeOdinContextConsumer>();
            var oc = consumer.GetOdinContext();
            tenant = oc.Tenant.DomainName;
        });

        // Assert
        Assert.AreEqual("frodo.baggins.mock", tenant);
    }

    //

    public class SomeOdinContextConsumer(OdinContextAccessor odinContextAccessor)
    {
        public OdinContext GetOdinContext()
        {
            return odinContextAccessor.GetCurrent();
        }
    }

}