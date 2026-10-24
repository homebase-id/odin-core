using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Odin.Core;

namespace Odin.Hosting.Extensions;

/// <summary>
/// Binds route and query values to <see cref="OdinSlug"/> by making one, so a malformed value fails
/// with the slug's own client error (400, <c>argumentError</c>), as it does in a JSON body, rather than
/// as an anonymous model-state error.
/// </summary>
public sealed class OdinSlugModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
        if (value == null && Nullable.GetUnderlyingType(bindingContext.ModelType) != null)
        {
            // An optional slug left out
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        bindingContext.Result = ModelBindingResult.Success(OdinSlug.Create(value, bindingContext.ModelName));
        return Task.CompletedTask;
    }
}

public sealed class OdinSlugModelBinderProvider : IModelBinderProvider
{
    public IModelBinder GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(OdinSlug) || context.Metadata.ModelType == typeof(OdinSlug?)
            ? new OdinSlugModelBinder()
            : null;
}
