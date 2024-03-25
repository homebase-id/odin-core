using System.ComponentModel.DataAnnotations;

namespace Odin.Core.Dto;

public class DevicePushNotificationRequest
{
    [Required]
    public string CorrelationId { get; set; } = "";

    [Required]
    public string DeviceToken { get; set; } = "";

    [Required]
    public string Title { get; set; } = "";

    [Required]
    public string Body { get; set; } = "";

    [Required]
    public string OriginDomain { get; set; } = "";

    [Required]
    public string Signature { get; set; } = "";
}

