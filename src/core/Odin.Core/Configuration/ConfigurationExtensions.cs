#nullable enable
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Odin.Core.Exceptions;
using Odin.Core.Util;

namespace Odin.Core.Configuration;

public class OdinConfigException(string message) : OdinSystemException(message);

public static class ConfigurationExtensions
{
    public static T Required<T>(this IConfiguration config, string path)
    {
        var section = config.GetSection(path);

        if (!section.Exists())
        {
            throw new OdinConfigException($"Missing config '{path}'");
        }

        return section.Get<T>()!;
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
        return value!;
    }

    //

    public static bool SectionExists(this IConfiguration config, string path)
    {
        var section = config.GetSection(path);
        return section.Exists();
    }

    //

    public static List<string> ExportAsEnvironmentVariables(this IConfiguration config)
    {
        var result = new List<string>();

        foreach (var section in config.AsEnumerable())
        {
            if (!string.IsNullOrEmpty(section.Key) && !string.IsNullOrEmpty(section.Value))
            {
                var key = section.Key.Replace(":", "__");
                var value = Env.EnvironmentVariableEscape(section.Value);
                result.Add($"{key}='{value}'");
            }
        }

        result.Sort();
        return result;
    }

    //
    
    public static SortedDictionary<string, string> ExportAsEnvironmentDictionary(this IConfiguration config)
    {
        var result = new SortedDictionary<string, string>();

        foreach (var section in config.AsEnumerable())
        {
            if (!string.IsNullOrEmpty(section.Key) && !string.IsNullOrEmpty(section.Value))
            {
                var key = section.Key.Replace(":", "__");
                var value = Env.EnvironmentVariableEscape(section.Value);
                result[key] = value;
            }
        }

        return result;
    }
    
    //
    
    
}
