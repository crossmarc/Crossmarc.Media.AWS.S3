using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.StaticFiles;
using OrchardCore.FileStorage;
using OrchardCore.Modules;

namespace Crossmarc.FileStorage.AWS.S3
{
    /// <summary>
    /// Provides an <see cref="IFileStore"/> implementation that targets an underlying AWS S3 bucket.
    /// </summary>
    /// <remarks>
    /// AWS S3 has different semantics for directories compared to a local file system.
    /// Directories are known as prefixes in S3 and are managed as S3 objects, much like files.
    /// </remarks>
    public class S3FileStore : IFileStore
    {
        /// <summary>
        /// The delimiter used to separate subdirectories as a <see cref="char"/>.
        /// </summary>
        public const char DirectoryDelimiter = '/';

        /// <summary>
        /// The delimiter used to separate subdirectories as a <see cref="string"/>.
        /// </summary>
        public const string DirectoryDelimiterString = "/";

        /// <summary>
        /// The error code returned by AWS S3 when an object key does not exist.
        /// </summary>
        private const string S3ErrorCodeNoSuchKey = "NoSuchKey";

        private readonly IAmazonS3 _s3Client;
        private readonly IClock _clock;
        private readonly IContentTypeProvider _contentTypeProvider;
        private readonly S3StorageOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="S3FileStore"/> class.
        /// </summary>
        /// <param name="s3Client">The <see cref="IAmazonS3"/> instance to use for S3 interactions.</param>
        /// <param name="options">The configuration options for the module.</param>
        /// <param name="clock">The <see cref="IClock"/> implementation for getting the current time.</param>
        /// <param name="contentTypeProvider">The <see cref="IContentTypeProvider"/> for determining content types.</param>
        public S3FileStore(IAmazonS3 s3Client, S3StorageOptions options, IClock clock, IContentTypeProvider contentTypeProvider)
        {
            _options = options;
            _clock = clock;
            _contentTypeProvider = contentTypeProvider;
            _s3Client = s3Client ?? new AmazonS3Client();
        }

        /// <summary>
        /// Asynchronously gets the information about the file at the specified path.
        /// </summary>
        /// <param name="path">The path for the file.</param>
        /// <returns>A task representing the asynchronous operation. When the operation completes, the result is an <see cref="IFileStoreEntry"/>.</returns>
        public async Task<IFileStoreEntry> GetFileInfoAsync(string path)
        {
            string objectKey = GetS3ObjectKey(_options, path);

            try
            {
                GetObjectResponse response = await _s3Client.GetObjectAsync(_options.BucketName, objectKey);

                return new S3File(_options, path, response);
            }
            catch (AmazonS3Exception ex)
            {
                if (string.Equals(ex.ErrorCode, S3ErrorCodeNoSuchKey))
                {
                    return null;
                }

                throw;
            }
        }

        /// <summary>
        /// Asynchronously gets the information about the directory at the specified path.
        /// </summary>
        /// <param name="path">The path for the directory.</param>
        /// <returns>A task representing the asynchronous operation. When the operation completes, the result is an <see cref="IFileStoreEntry"/>.</returns>
        public async Task<IFileStoreEntry> GetDirectoryInfoAsync(string path)
        {
            string objectKey = GetS3DirectoryPath(_options, path);

            // Determine whether at least one object is found for the directory. S3 doesn't
            // directly support checking for the existence of a directory, so a request is made
            // to list the contents at the prefix but limit the results to one item. If at least
            // one item exists (including the directory item itself), the directory exists.
            (List<S3Object> s3Objects, _) = await GetS3ObjectsAsync(objectKey, 1);

            if (s3Objects?.Count == 0)
            {
                return null;
            }

            return new S3Directory(path, _clock.UtcNow);
        }

        public async IAsyncEnumerable<IFileStoreEntry> GetDirectoryContentAsync(string path = null, bool includeSubDirectories = false)
        {
            var entries = new List<IFileStoreEntry>();

            await GetDirectoryContentAsync(path, includeSubDirectories, entries, null);

            foreach (IFileStoreEntry entry in entries.OrderBy(e => e.Path))
            {
                yield return entry;
            }
        }

        public async Task<bool> TryCreateDirectoryAsync(string path)
        {
            string objectKey = GetS3DirectoryPath(_options, path);

            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey
            };

            PutObjectResponse response = await _s3Client.PutObjectAsync(request);

            return response.HttpStatusCode.IsSuccessStatusCode();
        }

        public async Task<bool> TryDeleteFileAsync(string path)
        {
            try
            {
                string objectKey = GetS3ObjectKey(_options, path);

                var request = new DeleteObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = objectKey
                };

                DeleteObjectResponse response = await _s3Client.DeleteObjectAsync(request);

                return response.HttpStatusCode.IsSuccessStatusCode();
            }
            catch (AmazonS3Exception)
            {
                return false;
            }
        }

        public async Task<bool> TryDeleteDirectoryAsync(string path)
        {
            try
            {
                string prefix = GetS3DirectoryPath(_options, path);

                (List<S3Object> s3Objects, string continuationToken) = await GetS3ObjectsAsync(prefix);

                if (s3Objects.Count == 0)
                    return true;

                bool result = await TryDeleteObjectsAsync(_options.BucketName, s3Objects.Select(o => o.Key));

                while (result && continuationToken != null)
                {
                    (s3Objects, continuationToken) = await GetS3ObjectsAsync(prefix, continuationToken: continuationToken);

                    result = await TryDeleteObjectsAsync(_options.BucketName, s3Objects.Select(o => o.Key));
                }

                return result;
            }
            catch (AmazonS3Exception)
            {
                return false;
            }
        }

        public async Task MoveFileAsync(string oldPath, string newPath)
        {
            await CopyFileAsync(oldPath, newPath);
            await TryDeleteFileAsync(oldPath);
        }

        public async Task CopyFileAsync(string srcPath, string dstPath)
        {
            if (srcPath == dstPath)
                throw new ArgumentException($"The values for {nameof(srcPath)} and {nameof(dstPath)} must not be the same.");

            string sourceKey = GetS3ObjectKey(_options, srcPath);
            string destinationKey = GetS3ObjectKey(_options, dstPath);

            if (!await S3ObjectExistsAsync(_options.BucketName, sourceKey))
                throw new FileStoreException($"Cannot copy file '{srcPath}' because it does not exist.");

            if (await S3ObjectExistsAsync(_options.BucketName, destinationKey))
                throw new FileStoreException($"Cannot copy file '{srcPath}' because a file already exists in the new path '{dstPath}'.");

            var request = new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = sourceKey,
                DestinationBucket = _options.BucketName,
                DestinationKey = destinationKey
            };

            var response = await _s3Client.CopyObjectAsync(request);
        }

        public Task<Stream> GetFileStreamAsync(IFileStoreEntry fileStoreEntry)
        {
            return GetFileStreamAsync(fileStoreEntry.Path);
        }

        public async Task<Stream> GetFileStreamAsync(string path)
        {
            string objectKey = GetS3ObjectKey(_options, path);

            var request = new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey
            };

            var response = await _s3Client.GetObjectAsync(request);

            return response.ResponseStream;
        }

        public async Task<string> CreateFileFromStreamAsync(string path, Stream inputStream, bool overwrite = false)
        {
            string objectKey = GetS3ObjectKey(_options, path);

            if (!overwrite)
            {
                if (await S3ObjectExistsAsync(_options.BucketName, objectKey))
                {
                    throw new FileStoreException($"Cannot create file '{path}' because it already exists.");
                }
            }

            _contentTypeProvider.TryGetContentType(path, out string contentType);
            contentType = contentType ?? "application/octet-stream";

            PutObjectRequest request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                InputStream = inputStream,
                ContentType = contentType
            };

            try
            {
                PutObjectResponse response = await _s3Client.PutObjectAsync(request);

                return path;
            }
            catch (Exception ex)
            {
                throw new FileStoreException($"Error creating file '{path}'. {ex.Message}", ex);
            }
        }

        internal async Task GetDirectoryContentAsync(string path, bool includeSubDirectories, List<IFileStoreEntry> entries, string continuationToken)
        {
            var directories = new HashSet<string>();

            var request = new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = GetS3DirectoryPath(_options, path),
                ContinuationToken = continuationToken
            };

            // Use the delimiter to control whether "subdirectories" are included.
            if (!includeSubDirectories)
            {
                request.Delimiter = DirectoryDelimiterString;
            }

            ListObjectsV2Response response = await _s3Client.ListObjectsV2Async(request);

            // CommonPrefixes are only returned when restricting the results to the current "directory".
            foreach (var prefix in response.CommonPrefixes)
            {
                string relativePath = GetRelativePath(_options, prefix);
                directories.Add(relativePath);
            }

            foreach (var s3Object in response.S3Objects)
            {
                // S3 indicates "directories" by a trailing slash.
                if (s3Object.Key.EndsWith(DirectoryDelimiterString))
                {
                    string relativePath = GetRelativePath(_options, s3Object.Key.TrimEnd(DirectoryDelimiter));

                    // Avoid including the current path in the results. S3 may return the current path
                    // among the results of objects that match the specified prefix.
                    if (path != relativePath)
                    {
                        directories.Add(relativePath);
                    }
                }
                else
                {
                    var relativePath = GetRelativePath(_options, s3Object.Key);
                    entries.Add(new S3File(_options, relativePath, s3Object));

                    // When not using a request delimiter, S3 doesn't include separate S3 objects for "directories" that contain
                    // files. To ensure all parent paths are included in the results when subdirectories are included, each
                    // subdirectory is included here.
                    if (includeSubDirectories)
                    {
                        foreach (var directory in GetSubdirectories(relativePath))
                        {
                            directories.Add(directory);
                        }
                    }
                }
            }

            foreach (var directory in directories)
            {
                entries.Add(new S3Directory(directory, _clock.UtcNow));
            }

            if (response.ContinuationToken != null)
            {
                await GetDirectoryContentAsync(path, includeSubDirectories, entries, continuationToken);
            }
        }

        internal static string GetDirectoryPath(string path, string name)
        {
            return path.Length > name.Length ? path.Substring(0, path.Length - name.Length).TrimEnd(DirectoryDelimiter) : "";
        }

        internal static string GetRelativePath(S3StorageOptions options, string path)
        {
            if (!string.IsNullOrWhiteSpace(options?.Prefix) && !string.IsNullOrWhiteSpace(path))
            {
                return path.Replace(options.Prefix, string.Empty);
            }

            return path;
        }

        internal static string GetS3DirectoryPath(S3StorageOptions options, string path)
        {
            string fullPath = (options?.Prefix ?? "") + path;

            // S3 "directories" should end with a forward slash.
            if (!string.IsNullOrWhiteSpace(fullPath) && !fullPath.EndsWith(DirectoryDelimiterString))
            {
                fullPath += DirectoryDelimiter;
            }

            return fullPath;
        }

        internal static string GetS3ObjectKey(S3StorageOptions options, string path)
        {
            return (options?.Prefix ?? "") + path;
        }

        internal async Task<(List<S3Object>, string)> GetS3ObjectsAsync(string prefix, int maxKeys = 0, string continuationToken = null)
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken
            };

            if (maxKeys > 0)
            {
                request.MaxKeys = maxKeys;
            }

            ListObjectsV2Response response = await _s3Client.ListObjectsV2Async(request);

            var s3Objects = new List<S3Object>();

            foreach (var s3Object in response.S3Objects)
            {
                s3Objects.Add(s3Object);
            }

            return (s3Objects, response.ContinuationToken);
        }

        private IEnumerable<string> GetSubdirectories(string relativePath)
        {
            int startIndex = 0;
            int delimiterIndex;

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                yield break;
            }

            while ((delimiterIndex = relativePath.IndexOf(DirectoryDelimiter, startIndex)) > -1)
            {
                yield return relativePath.Substring(0, delimiterIndex);
                startIndex = delimiterIndex + 1;
            }
        }

        internal async Task<bool> S3ObjectExistsAsync(string bucketName, string objectKey)
        {
            try
            {
                var request = new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = objectKey
                };

                // If the object doesn't exist then a "NotFound" will be thrown
                await _s3Client.GetObjectMetadataAsync(request);

                return true;
            }
            catch (AmazonS3Exception e)
            {
                if (string.Equals(e.ErrorCode, "NoSuchBucket"))
                {
                    return false;
                }
                else if (string.Equals(e.ErrorCode, "NotFound"))
                {
                    return false;
                }

                throw;
            }
        }

        internal async Task<bool> TryDeleteObjectsAsync(string bucketName, IEnumerable<string> objectKeys)
        {
            List<KeyVersion> keyVersions = objectKeys.Select(k => new KeyVersion { Key = k }).ToList();

            if (keyVersions.Count == 0)
                return true;

            var request = new DeleteObjectsRequest
            {
                BucketName = bucketName,
                Objects = keyVersions
            };

            DeleteObjectsResponse response = await _s3Client.DeleteObjectsAsync(request);

            return response.HttpStatusCode.IsSuccessStatusCode();
        }
    }
}
