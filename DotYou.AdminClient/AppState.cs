using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DotYou.Types;
using DotYou.Types.Identity;
using Refit;

namespace DotYou.AdminClient
{
    public class AppState
    {
        private Guid _token;
        private readonly UserContext _user;
        private HttpClient _http;

        public AppState(HttpClient client)
        {
            _http = client;
            _user = new UserContext();
        }

        public UserContext User
        {
            get => _user;
        }

        public async Task<bool> Login(string password)
        {
            var authService = RestService.For<IAdminAuthenticationClient>(_http);
            var response = await authService.Authenticate(password);

            if (response.IsSuccessStatusCode)
            {
                _token = response.Content;
                await this.InitializeContext();
                return true;
            }

            return false;
        }
        
        public async Task InitializeContext()
        {
            try
            {
                var authService = RestService.For<IAdminAuthenticationClient>(_http);
                var identityService = RestService.For<IAdminIdentityAttributeClient>(_http);

                var response = await authService.IsValid(_token);
                
                await response.EnsureSuccessStatusCodeAsync();

                _user.IsAuthenticated = response.Content;
                
                if (_user.IsAuthenticated)
                {
                    var nameResponse = await identityService.GetPrimaryName();
//                    await nameResponse.EnsureSuccessStatusCodeAsync();
                    Console.WriteLine($"status: {nameResponse.StatusCode}");

                    // var uriResponse = await identityService.GetPrimaryAvatarUri();
                    // await uriResponse.EnsureSuccessStatusCodeAsync();


                    if (nameResponse.StatusCode != HttpStatusCode.NoContent)
                    {
                        var name = nameResponse.Content;
                        _user.Identity = "TODO";
                        _user.Surname = name.Surname;
                        _user.GivenName = name.Personal;
                    }

                    //_user.AvatarUri = uriResponse.Content;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to initialize YFContext", ex);
            }
        }
    }
}