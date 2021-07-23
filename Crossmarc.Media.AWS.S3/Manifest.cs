using Crossmarc.Media.AWS.S3;
using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = Constants.ModuleDisplayName,
    Author = "The Crossmarc Team",
    Website = "https://crossmarcsoftware.com",
    Version = "1.0.0"
)]

[assembly: Feature(
    Id = Constants.ModuleName,
    Name = Constants.ModuleDisplayName,
    Description = "Enables support for storing media files and serving them to clients directly from AWS S3.",
    Category = "Hosting",
    IsAlwaysEnabled = true
)]
