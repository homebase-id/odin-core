using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DotYou.Types;
using Refit;

namespace DotYou.AdminClient
{
    public class YFAppContext
    {
        public string GivenName { get; set; }
        public string Surname { get; set; }
        
        /// <summary>
        /// Specifies if this actor owns this website
        /// </summary>
        public bool IsIdentityOwner { get; set; }

        public bool IsAuthenticated { get; set; }
        
        /// <summary>
        /// The identity of the actor (i.e. frodobaggins.me, odin.vahalla.com)
        /// </summary>
        public string Identity { get; set; }

        public SubjectContext Subject { get; set; }
    }
    
    public class SubjectContext
    {
        public string GivenName { get; set; }

        public string Surname { get; set; }

        /// <summary>
        /// The identity of the subject (i.e. frodobaggins.me, odin.vahalla.com)
        /// </summary>
        public string Identifier { get; set; }
    }
    
    public class AppState
    {
        private YFAppContext _currentActor;
        private HttpClient _http;
        public AppState(HttpClient client)
        {
            _http = client;
        }

        public YFAppContext CurrentActor { get => _currentActor; set => _currentActor = value; }

        public async Task InitializeContext()
        {
            try
            {
                var authService = RestService.For<IAdminAuthenticationClient>(_http);

                //_currentActor = await _http.GetFromJsonAsync<YFAppContext>("/api/actorinfo");
                var ctx = new YFAppContext()
                {
                    Identity = "TODO",
                    Subject = new SubjectContext()
                    {
                        Identifier = "subject todo",
                        Surname = "subject surname",
                        GivenName = "subject given"
                    },
                    Surname = "actor surname",
                    GivenName = "actor givenname",
                    IsAuthenticated = true,
                    IsIdentityOwner = true
                };
                _currentActor = ctx;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to initialize YFContext", ex);
            }
        }
    }
}