using System;
using System.Diagnostics;
using Amazon.S3.Model;
using OrchardCore.FileStorage;

namespace Crossmarc.FileStorage.AWS.S3
{
    [DebuggerDisplay("File: {Name}, Path: {Path}")]
    public class S3File : IFileStoreEntry
    {
        public S3File(S3StorageOptions options, string path, GetObjectResponse response)
        {
            InitializeS3File(path,
                System.IO.Path.GetFileName(response.Key),
                response.ContentLength,
                response.LastModified);
        }

        public S3File(S3StorageOptions options, string path, S3Object s3Object)
        {
            InitializeS3File(path,
                System.IO.Path.GetFileName(s3Object.Key),
                s3Object.Size,
                s3Object.LastModified);
        }

        public S3File(string path, string name, long length, DateTime lastModifiedUtc)
        {
            InitializeS3File(path, name, length, lastModifiedUtc);
        }

        public string Path { get; private set; }

        public string Name { get; private set; }

        public string DirectoryPath { get; private set; }

        public long Length { get; private set; }

        public DateTime LastModifiedUtc { get; private set; }

        public bool IsDirectory => false;

        private void InitializeS3File(string path, string name, long length, DateTime lastModifiedUtc)
        {
            Path = path;
            Name = name;
            DirectoryPath = S3FileStore.GetDirectoryPath(path, name);
            Length = length;
            LastModifiedUtc = lastModifiedUtc;
        }
    }
}
