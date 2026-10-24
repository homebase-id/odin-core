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

        // A value left out is an absent slug, default(OdinSlug), as a JSON null is
        bindingContext.Result = ModelBindingResult.Success(
            value == null ? default : OdinSlug.Create(value, bindingContext.ModelName));
        return Task.CompletedTask;
    }
}

public sealed class OdinSlugModelBinderProvider : IModelBinderProvider
{
    private static readonly OdinSlugModelBinder Binder = new();

    public IModelBinder GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(OdinSlug) ? Binder : null;
}
