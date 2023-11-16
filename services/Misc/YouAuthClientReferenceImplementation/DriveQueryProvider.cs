using System.Net;
using System.Web;
using Odin.Core.Services.Drives;

namespace YouAuthClientReferenceImplementation;

public class DriveQueryProvider
{
    public async Task<QueryBatchResponse> QueryBatch(
        string domain,
        Cookie cat,
        string sharedSecret,
        string driveAlias,
        string driveType)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-ODIN-FILE-SYSTEM-TYPE", "Standard");
        client.DefaultRequestHeaders.Add("Cookie", cat.ToString());

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["maxRecords"] = "1000";
        qs["includeMetadataHeader"] = "true";
        qs["alias"] = driveAlias;
        qs["type"] = driveType;
        qs["fileState"] = "1";

        var url = $"https://{domain}/api/apps/v1/drive/query/batch?{qs}";

        url = Helper.UriWithEncryptedQueryString(url, sharedSecret);
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.OK)
        {
            try
            {
                return Helper.DecryptContent<QueryBatchResponse>(content, sharedSecret);
            }
            catch (Exception e)
            {
                throw new Exception($"Oh no {(int)response.StatusCode}: {e.Message}", e);
            }
        }

        string json;
        try
        {
            json = Helper.DecryptContent(content, sharedSecret);
        }
        catch (Exception e)
        {
            throw new Exception($"Oh no {(int)response.StatusCode}: {content}", e);
        }
        throw new Exception($"Oh no {(int)response.StatusCode}: {json}");

    }

}