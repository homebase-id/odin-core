using System;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System;

namespace Odin.Services.JobManagement;

#nullable enable

public class JobApiResponse
{
    public Guid? JobId { get; set; }
    public JobState? State { get; set; }
    public string? Error { get; set; }
    public string? Data { get; set; }

    public static JobApiResponse Deserialize(string json)
    {
        var result = OdinSystemSerializer.Deserialize<JobApiResponse>(json);
        if (result == null)
        {
            throw new OdinSystemException("Error deserializing JobApiResponse");
        }

        return result;
    }

    //

    public static (JobApiResponse, T?) Deserialize<T>(string json) where T : class
    {
        var response = Deserialize(json);

        if (response.Data == null)
        {
            return (response, null);
        }

        var data = OdinSystemSerializer.Deserialize<T>(response.Data);
        if (data == null)
        {
            throw new OdinSystemException("Error deserializing JobApiResponse.Data");
        }

        return (response, data);
    }

    //

}

