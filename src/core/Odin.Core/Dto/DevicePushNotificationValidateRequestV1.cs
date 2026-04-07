using System;
using System.ComponentModel.DataAnnotations;

namespace Odin.Core.Dto;

#nullable enable

public class DevicePushNotificationValidateRequestV1
{
    public int Version { get; } = 1;

    [Required]
    public string DevicePlatform { get; set; } = "";

    [Required]
    public string DeviceToken { get; set; } = "";

    [Required]
    public string OriginDomain { get; set; } = "";

    [Required]
    public byte[] Signature { get; set; } = Array.Empty<byte>();

    [Required]
    public string Id { get; set; } = "";

    [Required]
    public string Timestamp { get; set; } = "";

    [Required]
    public string CorrelationId { get; set; } = "";
}
