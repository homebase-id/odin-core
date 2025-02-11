using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Services.LinkPreview;

public class LinkPreviewService(IFusionCache globalCache, ILevel1Cache<LinkPreviewService> tenantCache)
{
    private const string IndexFileKey = "link-preview-service-index-file";
    private const string GenericLinkPreviewCacheKey = "link-preview-service-index-file";
    private const string PostSpecificLinkPreviewCacheKey = "link-preview-service-index-file";

    public async Task InjectMetadata(string indexFilePath)
    {
        var cache = await tenantCache.GetOrSetAsync<string>(
            GenericLinkPreviewCacheKey,
            _ => LoadIndexFileTemplate(indexFilePath),
            TimeSpan.FromSeconds(30)
        );

        //TODO: seb will ensure this is local to server only
        var indexTemplate = await globalCache.GetOrSetAsync<string>(
            IndexFileKey,
            _ => LoadIndexFileTemplate(indexFilePath),
            TimeSpan.FromSeconds(30)
        );
    }

    private Task<string> LoadIndexFileTemplate(string path)
    {
        throw new NotImplementedException();
    }


    private Task<(string thumbnailPath, string caption)> GeneratePreview()
    {
        /*
         *
         * see posttypes.ts in odin-js
         * interface is export interface PostContent
         *  type is tweet or article data is small enough, there is no payload for the content.
         *  so you must read the json header and the payload with key 'dflt_key'
         *
         * image: see postContent.PrimaryMediaFile
             * export interface PrimaryMediaFile {
                 fileKey: string;  // this is the payload key
                 fileId: string  // ignore this (it was pre multi-payload support)
                 type: string; // mime-type
               }
         */

        return Task.FromResult(("", ""));
    }
}