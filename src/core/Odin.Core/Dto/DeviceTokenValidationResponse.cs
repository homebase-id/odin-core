namespace Odin.Core.Dto;

#nullable enable

public class DeviceTokenValidationResponse
{
    public bool Valid { get; set; }
    public string? Reason { get; set; }
}
