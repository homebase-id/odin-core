#nullable enable
using Microsoft.Extensions.Configuration;
using Odin.Core.Exceptions;

namespace Odin.Core.Configuration;

public static class ConfigurationExtensions
{
    public static T Required<T>(this IConfiguration config, string path)
    {
        var section = config.GetSection(path);

        if (!section.Exists())
        {
            throw new OdinSystemException($"Missing config '{path}'");
        }

        return section.Get<T>();
    }
    
    //

    public static T GetOrDefault<T>(this IConfiguration config, string path, T defaultValue = default!)
    {
        var section = config.GetSection(path);
    
        if (!section.Exists())
        {
            return defaultValue;
        }

        var value = section.Get<T>();
        return value;
    }

    //

    public static bool SectionExists(this IConfiguration config, string path)
    {
        var section = config.GetSection(path);
        return section.Exists();
    }

    //

}
