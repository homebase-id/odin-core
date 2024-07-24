using Odin.Core.Exceptions;
using Odin.Core.Serialization;

namespace Odin.Services.JobManagement;

#nullable enable

public class OldJobResponse
{
    public OldJobStatus Status { get; set; }
    public string JobKey { get; set; } = "";
    public string? Error { get; set; }
    public string? Data { get; set; }

    //

    public static OldJobResponse Deserialize(string json)
    {
        var result = OdinSystemSerializer.Deserialize<OldJobResponse>(json);
        if (result == null)
        {
            throw new OdinSystemException("Error deserializing OldJobResponse");
        }

        return result;
    }

    //

    public static (OldJobResponse, T?) Deserialize<T>(string json) where T : class
    {
        var response = Deserialize(json);

        if (response.Data == null)
        {
            return (response, null);
        }

        var data = OdinSystemSerializer.Deserialize<T>(response.Data);
        if (data == null)
        {
            throw new OdinSystemException("Error deserializing OldJobResponse.Data");
        }

        return (response, data);
    }

    //

}

