﻿using System;

namespace Youverse.Core.Services.Authorization.AppRegistration
{
    public class AppRegistration
    {
        public Guid ApplicationId { get; set; }
        public string Name { get; set; }
        public Guid Id { get; internal set; }
    }
}