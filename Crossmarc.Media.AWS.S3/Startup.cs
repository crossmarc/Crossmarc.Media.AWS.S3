using System;
using System.IO;
using Amazon.S3;
using Crossmarc.FileStorage.AWS.S3;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Configuration;
using OrchardCore.Media;
using OrchardCore.Media.Core;
using OrchardCore.Media.Events;
using OrchardCore.Modules;

namespace Crossmarc.Media.AWS.S3
{
    [Feature(Constants.ModuleName)]
    public class Startup : OrchardCore.Modules.StartupBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly ILogger<Startup> _logger;
        private readonly IShellConfiguration _shellConfiguration;

        public Startup(
            IConfiguration configuration,
            IHostEnvironment environment,
            ILogger<Startup> logger,
            IShellConfiguration shellConfiguration
            )
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
            _shellConfiguration = shellConfiguration;
        }

        public override int Order => 10;

        public override void ConfigureServices(IServiceCollection services)
        {
            services.Configure<MediaStorageOptions>(_shellConfiguration.GetSection(Constants.ModuleName));

            // Only replace default implementation if options are valid.
            var bucketName = _shellConfiguration[$"{Constants.ModuleName}:{nameof(MediaStorageOptions.BucketName)}"];
            var prefix = _shellConfiguration[$"{Constants.ModuleName}:{nameof(MediaStorageOptions.Prefix)}"];

            if (CheckOptions(bucketName, _logger))
            {
                // Register a media cache file provider.
                services.AddSingleton<IMediaFileStoreCacheFileProvider>(serviceProvider =>
                {
                    var hostingEnvironment = serviceProvider.GetRequiredService<IWebHostEnvironment>();

                    if (string.IsNullOrWhiteSpace(hostingEnvironment.WebRootPath))
                    {
                        throw new Exception("The wwwroot folder for serving cache media files is missing.");
                    }

                    var mediaOptions = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
                    var shellOptions = serviceProvider.GetRequiredService<IOptions<ShellOptions>>();
                    var shellSettings = serviceProvider.GetRequiredService<ShellSettings>();
                    var logger = serviceProvider.GetRequiredService<ILogger<DefaultMediaFileStoreCacheFileProvider>>();

                    var mediaCachePath = GetMediaCachePath(hostingEnvironment, DefaultMediaFileStoreCacheFileProvider.AssetsCachePath, shellSettings);

                    if (!Directory.Exists(mediaCachePath))
                    {
                        Directory.CreateDirectory(mediaCachePath);
                    }

                    return new DefaultMediaFileStoreCacheFileProvider(logger, mediaOptions.AssetsRequestPath, mediaCachePath);
                });

                // Replace the default media file provider with the media cache file provider.
                services.Replace(ServiceDescriptor.Singleton<IMediaFileProvider>(serviceProvider =>
                    serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>()));

                // Register the media cache file provider as a file store cache provider.
                services.AddSingleton<IMediaFileStoreCache>(serviceProvider =>
                    serviceProvider.GetRequiredService<IMediaFileStoreCacheFileProvider>());

                // Register the IAmazonS3 client for obtaining media assets from AWS S3 unless a singleton
                // has already been registered for interaction with AWS S3.
                var awsOptions = _configuration.GetAWSOptions();
                services.TryAddSingleton(s => awsOptions.CreateServiceClient<IAmazonS3>());

                // Replace the default media file store with an AWS S3 file store.
                services.Replace(ServiceDescriptor.Singleton<IMediaFileStore>(serviceProvider =>
                {
                    IAmazonS3 s3Client = serviceProvider.GetRequiredService<IAmazonS3>();

                    var options = serviceProvider.GetRequiredService<IOptions<MediaStorageOptions>>().Value;
                    var mediaOptions = serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value;
                    var clock = serviceProvider.GetRequiredService<IClock>();
                    var contentTypeProvider = serviceProvider.GetRequiredService<IContentTypeProvider>();
                    var mediaEventHandlers = serviceProvider.GetServices<IMediaEventHandler>();
                    var mediaCreatingEventHandlers = serviceProvider.GetServices<IMediaCreatingEventHandler>();
                    var logger = serviceProvider.GetRequiredService<ILogger<DefaultMediaFileStore>>();

                    var fileStore = new S3FileStore(s3Client, options, clock, contentTypeProvider);

                    var baseUrl = mediaOptions.CdnBaseUrl;

                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        // TODO: Confirm N.Virginia bucket works correctly with region name in url.
                        baseUrl = $"https://s3-{awsOptions.Region.SystemName}.amazonaws.com/{options.BucketName}/";
                    }

                    if (!baseUrl.EndsWith(S3FileStore.DirectoryDelimiterString))
                    {
                        baseUrl += S3FileStore.DirectoryDelimiterString;
                    }

                    return new DefaultMediaFileStore(fileStore, options.Prefix, baseUrl, mediaEventHandlers, mediaCreatingEventHandlers, logger);
                }));
            }
        }

        private string GetMediaCachePath(IWebHostEnvironment hostingEnvironment, string assetsPath, ShellSettings shellSettings)
        {
            return PathExtensions.Combine(hostingEnvironment.WebRootPath, assetsPath, shellSettings.Name);
        }

        private static bool CheckOptions(string bucketName, ILogger logger)
        {
            var optionsAreValid = true;

            if (string.IsNullOrWhiteSpace(bucketName))
            {
                logger.LogError($"{Constants.ModuleDisplayName} is enabled but not active because {nameof(MediaStorageOptions.BucketName)} is missing or empty in application configuration.");
                optionsAreValid = false;
            }

            return optionsAreValid;
        }
    }
}
