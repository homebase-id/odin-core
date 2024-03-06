#nullable enable
using System.Text;

namespace Odin.Core.Util
{
    public static class UrlUtil
    {
        public static string UrlAppend(this string baseUrl, params string[] segments)
        {
            if (segments.Length == 0)
            {
                return baseUrl;
            }

            var sb = new StringBuilder(baseUrl.TrimEnd('/'));

            for (var idx = 0; idx < segments.Length - 1; idx++)
            {
                sb.Append('/').Append(segments[idx].Trim('/'));
            }

            var lastItem = segments[^1];
            if (lastItem.Length > 0)
            {
                var lead = lastItem[0];
                if (lead is not ('?' or '#'))
                {
                    sb.Append('/');
                }
                sb.Append(lastItem);
            }

            return sb.ToString();
        }
    }
}
