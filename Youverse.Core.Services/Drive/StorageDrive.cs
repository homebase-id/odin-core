using System;
using System.IO;
using LiteDB;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Information about a drive
    /// </summary>
    public sealed class StorageDrive : StorageDriveBase
    {
        private readonly string _root;
        private readonly StorageDriveBase _inner;

        public StorageDrive(string root, StorageDriveBase inner)
        {
            _inner = inner;
            _root = Path.Combine(root, this.Id.ToString("N"));
        }

        public string RootPath => this._root;

        public override Guid Id
        {
            get => _inner.Id;
            init { }
        }

        public override string Name
        {
            get => _inner.Name;
            set { }
        }
    }

    public class StorageDriveBase
    {
        public virtual Guid Id { get; init; }

        public virtual string Name { get; set; }
    }
}