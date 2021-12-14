using System;

namespace Youverse.Hosting.Controllers.Owner.Demo
{
    internal class ImportedContact
    {
        public Guid Id { get; set; }
        public string GivenName { get; set; }
        public string Surname { get; set; }
        public string PrimaryEmail { get; set; }

        public string Tag { get; set; }
    }
}