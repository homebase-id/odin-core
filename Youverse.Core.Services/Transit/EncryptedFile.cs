using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Youverse.Core.Services.Transit
{
    public class EncryptedFile
    {
        private string _fileRoot;
        private KeyHeader _keyHeader;

        public EncryptedFile(string fileRoot)
        {
            _fileRoot = fileRoot;
            this.Id = Guid.NewGuid();
        }

        public EncryptedFile(Guid id, string fileRoot)
        {
            _fileRoot = fileRoot;
            this.Id = id;
        }

        public Guid Id { get; }

        public string MetadataFileName => this.Id + ".mdata";
        public string DataFileName => this.Id + ".data";
        public string HeaderFileName => this.Id + ".hdr";

        public string MetaDataPath => Path.Combine(_fileRoot, this.MetadataFileName);
        public string DataFilePath => Path.Combine(_fileRoot, this.DataFileName);
        public string HeaderPath => Path.Combine(_fileRoot, this.HeaderFileName);

        /// <summary>
        /// Returns an instance of the KeyHeader from disk.
        /// </summary>
        /// <returns></returns>
        public async Task<KeyHeader> GetKeyHeader()
        {
            if (null == _keyHeader)
            {
                var json = await File.OpenText(this.HeaderPath).ReadToEndAsync();
                _keyHeader = JsonConvert.DeserializeObject<KeyHeader>(json);
            }

            return _keyHeader;
        }
    }
}