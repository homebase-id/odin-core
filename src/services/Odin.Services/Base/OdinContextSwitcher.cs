using System;

namespace Odin.Services.Base;

public class OdinContextSwitcher : IDisposable
{
    private readonly IOdinContextAccessor _contextAccessor;

    public OdinContextSwitcher(IOdinContextAccessor contextAccessor, OdinContext context)
    {
        _contextAccessor = contextAccessor;
        contextAccessor.SetCurrent(context);
    }
    public void Dispose()
    {
        _contextAccessor.SetCurrent(null);
    }
}