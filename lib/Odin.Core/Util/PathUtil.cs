using System.IO;
using System.Runtime.InteropServices;

namespace Odin.Core.Util
{
    public static class PathUtil
    {
        /// <summary>
        /// Switches the path to match the currently running OS
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string OsIfy(string path)
        {
            char target = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? '/' : '\\';
            return path.Replace(target, Path.DirectorySeparatorChar);
        }

        public static string Combine(params string[] paths)
        {
            return OsIfy(Path.Combine(paths));
        }
    }
}