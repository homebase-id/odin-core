using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;

#nullable enable
namespace Odin.Services.Base;

public class OdinContextRootContainer(ILifetimeScope scope)
{
    public ILifetimeScope Scope { get; } = scope;
}

public class OdinContextAccessor(IHttpContextAccessor httpContextAccessor, OdinContextRootContainer odinContextRootContainer)
{
    private static readonly AsyncLocal<ILifetimeScope> AsyncLocalScope = new();
    private static ILifetimeScope? LifetimeScope
    {
        get => AsyncLocalScope.Value;
        set => AsyncLocalScope.Value = value!;
    }

    //

    public OdinContext GetCurrent()
    {
        // Try to resolve OdinContext from the current scope if available
        if (LifetimeScope != null && LifetimeScope.TryResolve<OdinContext>(out var foo))
        {
            return foo;
        }

        // Fallback to resolving from HttpContext
        foo = httpContextAccessor.HttpContext?.RequestServices.GetService<OdinContext>();
        if (foo != null)
        {
            return foo;
        }

        throw new OdinSystemException("No OdinContext found. Did you forget to create a new scope?");
    }

    //

    public async Task ExecuteInScope(OdinContext odinContext, Func<Task> operations)
    {
        var taskScope = odinContextRootContainer.Scope.BeginLifetimeScope(builder =>
        {
            builder.RegisterInstance(odinContext).As<OdinContext>();
        });

        LifetimeScope = taskScope ?? throw new OdinSystemException("No scope available");

        try
        {
            await operations();
        }
        finally
        {
            LifetimeScope = null;
            taskScope.Dispose();
        }
    }

    //

}