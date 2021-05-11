using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using Blazor.AdminLte;
using Blazored.LocalStorage;
using Blazored.Toast;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotYou.AdminClient
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.Services.AddBlazoredLocalStorage();

            builder.Services.AddScoped(sp => new HttpClient {BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)});
            builder.Services.AddSingleton<AppState>(svc =>
            {
                var client =svc.GetRequiredService<HttpClient>();
                var storage = svc.GetRequiredService<ILocalStorageService>();
                var state = new AppState(client, storage);
                return state;
            });
            
            builder.Services.AddAdminLte();
            builder.Services.AddBlazoredToast();
            
            var host = builder.Build();

            var stateService = host.Services.GetRequiredService<AppState>();
            await stateService.InitializeContext();

            await host.RunAsync();

        }
    }
}