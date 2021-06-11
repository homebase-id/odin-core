using System;
using System.Threading.Tasks;
using Blazor.AdminLte;
using Blazored.LocalStorage;
using Blazored.Toast;
using DotYou.AdminClient.Services;
using DotYou.Types.ApiClient;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
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

            Uri baseUri = new Uri(builder.HostEnvironment.BaseAddress);
            //Uri baseUri = new Uri("https://frodobaggins.me");

            builder.Services.AddRefitClient<IOwnerAuthenticationClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = baseUri; });

            builder.Services.AddSingleton<AuthState>(svc =>
            {
                var client = svc.GetRequiredService<IOwnerAuthenticationClient>();
                var storage = svc.GetRequiredService<ILocalStorageService>();
                var js = svc.GetRequiredService<IJSRuntime>();
                var state = new AuthState(client, storage, js);
                return state;
            });

            builder.Services.AddTransient<AuthTokenMessageHandler>();

            builder.Services.AddRefitClient<IAdminIdentityAttributeClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = baseUri; })
                .AddHttpMessageHandler<AuthTokenMessageHandler>();
                
            builder.Services.AddRefitClient<ICircleNetworkClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = baseUri; })
                .AddHttpMessageHandler<AuthTokenMessageHandler>();
            
            builder.Services.AddRefitClient<IContactManagementClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = baseUri; })
                .AddHttpMessageHandler<AuthTokenMessageHandler>();
            
            builder.Services.AddRefitClient<IEmailMessageClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = baseUri; })
                .AddHttpMessageHandler<AuthTokenMessageHandler>();

            builder.Services.AddRefitClient<IDemoDataClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = baseUri; })
                .AddHttpMessageHandler<AuthTokenMessageHandler>();

            builder.Services.AddRefitClient<IChatClient>()
                .ConfigureHttpClient(client => { client.BaseAddress = baseUri; })
                .AddHttpMessageHandler<AuthTokenMessageHandler>();
                
            builder.Services.AddSingleton<AppState>(svc =>
            {
                var auth = svc.GetRequiredService<AuthState>();
                var client = svc.GetRequiredService<IAdminIdentityAttributeClient>();
                var nav = svc.GetRequiredService<NavigationManager>();
                var events = svc.GetRequiredService<IClientNotificationEvents>();
                
                var state = new AppState(auth, client, nav, events);
                return state;
            });
            
            builder.Services.AddSingleton<IClientNotificationEvents, ClientNotificationEvents>();
            
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