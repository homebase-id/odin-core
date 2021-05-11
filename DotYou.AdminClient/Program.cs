using System;
using System.Threading.Tasks;
using Blazor.AdminLte;
using Blazored.LocalStorage;
using Blazored.Toast;
using DotYou.Types;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace DotYou.AdminClient
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.Services.AddBlazoredLocalStorage();

            builder.Services.AddRefitClient<IAdminAuthenticationClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress); });

            builder.Services.AddSingleton<AuthState>(svc =>
            {
                var client = svc.GetRequiredService<IAdminAuthenticationClient>();
                var storage = svc.GetRequiredService<ILocalStorageService>();
                var state = new AuthState(client, storage);
                return state;
            });

            builder.Services.AddTransient<AuthTokenMessageHandler>();

            builder.Services.AddRefitClient<IAdminIdentityAttributeClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress); })
                .AddHttpMessageHandler<AuthTokenMessageHandler>();
                
            builder.Services.AddRefitClient<ICircleNetworkRequestsClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress); })
                .AddHttpMessageHandler<AuthTokenMessageHandler>();
            
            builder.Services.AddSingleton<AppState>(svc =>
            {
                var auth = svc.GetRequiredService<AuthState>();
                var client = svc.GetRequiredService<IAdminIdentityAttributeClient>();
                var storage = svc.GetRequiredService<ILocalStorageService>();
                var state = new AppState(auth, client, storage);
                return state;
            });
            
            builder.Services.AddAdminLte();
            builder.Services.AddBlazoredToast();

            var host = builder.Build();
            var authState = host.Services.GetRequiredService<AuthState>();
            await authState.InitializeContext();

            var appState = host.Services.GetRequiredService<AppState>();
            await appState.InitializeContext();

            await host.RunAsync();
        }
    }
}