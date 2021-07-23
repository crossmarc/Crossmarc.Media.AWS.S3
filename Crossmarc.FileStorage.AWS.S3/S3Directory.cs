using System;
using System.Diagnostics;
using Amazon.S3.Model;
using OrchardCore.FileStorage;

namespace Crossmarc.FileStorage.AWS.S3
{
    [DebuggerDisplay("Directory: {Name}, Path: {Path}")]
    public class S3Directory : IFileStoreEntry
    {
        public S3Directory(string path, DateTime lastModifiedUtc)
        {
            Path = path.TrimEnd(S3FileStore.DirectoryDelimiter);
            Name = System.IO.Path.GetFileName(Path);
            DirectoryPath = S3FileStore.GetDirectoryPath(Path, Name);
            LastModifiedUtc = lastModifiedUtc;
        }

        public S3Directory(S3StorageOptions options, string path, GetObjectResponse response)
        {
            Path = path.TrimEnd(S3FileStore.DirectoryDelimiter);
            Name = System.IO.Path.GetFileName(response.Key.TrimEnd(S3FileStore.DirectoryDelimiter));
            DirectoryPath = S3FileStore.GetDirectoryPath(Path, Name);
            LastModifiedUtc = response.LastModified;
        }

        public string Path { get; }

        public string Name { get; }

        public string DirectoryPath { get; }

        public long Length => 0;

        public DateTime LastModifiedUtc { get; }

        public bool IsDirectory => true;
    }
}
