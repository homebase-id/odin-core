using System;
using System.IO;

namespace Youverse.Core.Services.Transit
{
    public class TenantFile
    {
        private string _tenantFileRoot;

        public TenantFile(string tenantFileRoot)
        {
            _tenantFileRoot = tenantFileRoot;
            this.Id = Guid.NewGuid();
        }

        public TenantFile(Guid id, string tenantFileRoot)
        {
            _tenantFileRoot = tenantFileRoot;
            this.Id = id;
        }

        public Guid Id { get; }

        public string Metadata => this.Id + ".mdata";
        public string DataFile => this.Id + ".data";
        public string Header => this.Id + ".hdr";

        public string MetaDataPath => Path.Combine(_tenantFileRoot, this.Metadata);
        public string DataFilePath => Path.Combine(_tenantFileRoot, this.DataFile);
        public string HeaderPath => Path.Combine(_tenantFileRoot, this.Header);
    }
}