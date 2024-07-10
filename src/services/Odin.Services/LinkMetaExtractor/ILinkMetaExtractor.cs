using System.Threading.Tasks;

namespace Odin.Services.LinkMetaExtractor;

public interface ILinkMetaExtractor
{
    Task<LinkMeta> ExtractAsync(string url);
}