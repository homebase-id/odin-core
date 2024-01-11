using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Configuration;


namespace Odin.Hosting.Controllers.Anonymous;

[ApiController]
public class HostingCompanyAnonymousConfigController : Controller
{
    private readonly OdinConfiguration _configuration;

    /// <summary />
    public HostingCompanyAnonymousConfigController(OdinConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet("config/reporting")]
    public ContentReportingConfig GetReportContentUrl()
    {
        // Accessible from anywhere, as any identity can request the report url of this user;
        HttpContext.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
        return new ContentReportingConfig()
        {
            Url = _configuration.Host.ReportContentUrl
        };
    }
}

public record ContentReportingConfig
{
    public string Url { get; set; }
}
