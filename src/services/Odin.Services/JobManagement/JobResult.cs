using System;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;

namespace Odin.Services.JobManagement;

#nullable enable

public class JobResult
{
    public Guid JobId { get; set; }
    public JobState Status { get; set; }
    public string? Error { get; set; }
    public string? Data { get; set; }

    //

    public static JobResult Deserialize(string json)
    {
        var result = OdinSystemSerializer.Deserialize<JobResult>(json);
        if (result == null)
        {
            throw new OdinSystemException("Error deserializing JobResult");
        }

        return result;
    }

    //

    public static (JobResult, T?) Deserialize<T>(string json) where T : class
    {
        var response = Deserialize(json);

        if (response.Data == null)
        {
            return (response, null);
        }

        var data = OdinSystemSerializer.Deserialize<T>(response.Data);
        if (data == null)
        {
            throw new OdinSystemException("Error deserializing JobResult.Data");
        }

        return (response, data);
    }

    //

}

