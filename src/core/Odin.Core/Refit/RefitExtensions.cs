using System;
using System.Threading.Tasks;
using Refit;

namespace Odin.Core.Refit;
#nullable enable

public static class RefitExtensions
{
    public static async Task<T?> TryGetContentAsAsync<T>(this ApiException exception) where T : class
    {
        try
        {
            return await exception.GetContentAsAsync<T>();
        }
        catch (Exception)
        {
            return default;
        }
    }
}
