using System;
using Microsoft.AspNetCore.Http;

namespace Odin.Core.Services.AppNotifications.Push;

public static class PushNotificationCookieUtil
{
    private const string DevicePushNotificationCookieName = "dpnc";
    
    public static Guid? GetDeviceKey(HttpRequest request)
    {
        var hasCookie = request.Cookies.TryGetValue(DevicePushNotificationCookieName, out var value);
        if (hasCookie && Guid.TryParse(value, out var valueAsGuid))
        {
            return valueAsGuid;
        }

        return null;
    } 

    public static void EnsureDeviceCookie(HttpContext context)
    {
        var existingCookie = GetDeviceKey(context.Request);
        if (null != existingCookie)
        {
            return;
        }
        
        var options = new CookieOptions()
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddYears(100)
        };

        context.Response.Cookies.Append(DevicePushNotificationCookieName, Guid.NewGuid().ToString(), options);
    }
}