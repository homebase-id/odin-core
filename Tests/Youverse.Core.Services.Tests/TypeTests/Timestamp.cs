using System;
using NUnit.Framework;
using SQLitePCL;

namespace Youverse.Core.Services.Tests.TypeTests;

public struct Timestamp
{
    public long EpochTimeUtc { get; set; }

    public TimeZoneInfo Timezone { get; set; }
}

public class TimestampTests
{
    // [Test]
    // public void Test1()
    // {
    //     string url_encoded = "MIIBIDANBgkqhkiG9w0BAQEFAAOCAQ0AMIIBCAKCAQEAsT7JRa0RJtpbeQutDZvnLIULJwKpUW32X757NJkwukZjbkFMDL5aso5xn5LzNXK6P1gw8tSitBMYGHvMKsxiyEmjiznutjmjw5HWY0bKsG2anXYyYgUsephArZSxgf%2FK0eIA0M9TiM1oaZZeVJvdGhGp86LovOq5l0dI26TMuIuzwPpr9cX7NVft3YfdZ2zFcv5RWoTxckRHVnixaWiToWRy23Asa8X1XQEcN7FT7lIxJqcatxOnjPwy4YARGtqxuGmz3dUUewfk86%2B5%2B3KN3IzbSTtCCqCWHLVbZXsui6fbJPkyCQOkESoDTlSGy%2F%2FGkBrEzp7wEfDC2u%2BZsZisXQIBAw%3D%3D";
    //     string rawb64 =
    //         "MIIBIDANBgkqhkiG9w0BAQEFAAOCAQ0AMIIBCAKCAQEAsT7JRa0RJtpbeQutDZvnLIULJwKpUW32X757NJkwukZjbkFMDL5aso5xn5LzNXK6P1gw8tSitBMYGHvMKsxiyEmjiznutjmjw5HWY0bKsG2anXYyYgUsephArZSxgf/K0eIA0M9TiM1oaZZeVJvdGhGp86LovOq5l0dI26TMuIuzwPpr9cX7NVft3YfdZ2zFcv5RWoTxckRHVnixaWiToWRy23Asa8X1XQEcN7FT7lIxJqcatxOnjPwy4YARGtqxuGmz3dUUewfk86+5+3KN3IzbSTtCCqCWHLVbZXsui6fbJPkyCQOkESoDTlSGy//GkBrEzp7wEfDC2u+ZsZisXQIBAw==";
    //
    //     var ascii_urldecode = System.Web.HttpUtility.UrlDecode(url_encoded);
    //     var unicode_urldecode = System.Web.HttpUtility.UrlEncodeUnicode(url_encoded);
    //
    //     var same = ascii_urldecode == unicode_urldecode;
    //     var same2 = ascii_urldecode == rawb64;
    //     var same3 = unicode_urldecode == rawb64;
    //     
    //     var bytes = Convert.FromBase64String(unicode_urldecode);
    // }
}