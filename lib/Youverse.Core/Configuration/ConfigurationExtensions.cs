using Microsoft.Extensions.Configuration;
using Youverse.Core.Exceptions;

#nullable enable
namespace Youverse.Core.Configuration
{
    public static class ConfigurationExtensions
    {
        public static T Required<T>(this IConfiguration config, string path)
        {
            var section = config.GetSection(path);

            if (!section.Exists())
            {
                throw new YouverseSystemException($"Missing config '{path}'");
            }

            return section.Get<T>();
        }
    }
}
