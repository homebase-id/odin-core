using System;

namespace Youverse.Core.Services.Authentication.AppAuth
{
    public class AuthCodeExchangeRequest
    {
        public Guid AuthCode { get; set; }

        public AppDevice AppDevice { get; set; }
    }
}