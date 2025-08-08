using System.Text.Json;

using Microsoft.Extensions.Logging;

using Migration.Toolkit.Data.Configuration;
using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;

namespace Migration.Toolkit.Data.Providers;
internal class TypeProvider(SitefinityDataConfiguration configuration, ILogger<TypeProvider> logger) : ITypeProvider
{
    private readonly string[] excludedFileNames = ["version.sf", "configs.sf", "widgetTemplates.sf"];
    private readonly string[] sitefinityTypeDirectories = ["Blogs", "Events", "Lists", "News"];
    private readonly string staticSitefinityTypesDirectory = Path.Combine(AppContext.BaseDirectory, "StaticSitefinityTypes");

    public IEnumerable<SitefinityType> GetAllTypes()
    {
        var sitefinityTypes = new List<SitefinityType>();

        sitefinityTypes.AddRange(GetDynamicModuleTypes());
        sitefinityTypes.AddRange(GetSitefinityTypes());
        sitefinityTypes.AddRange(GetMediaContentTypes());

        return sitefinityTypes;
    }

    public IEnumerable<SitefinityType> GetMediaContentTypes()
    {
        var mediaTypes = new List<SitefinityType>();

        // Create Image content type
        var imageFields = CreateImageFields();
        var imageType = new StaticSitefinityType
        {
            Id = Guid.Parse("EBEA5B71-0896-4C6D-A390-51605AB3FA94"),
            DisplayName = "Image",
            ClassName = "Image",
            Namespace = "Migration.Toolkit.Media",
            Fields = imageFields,
            LastModified = DateTime.Now
        };
        mediaTypes.Add(imageType);

        // Create Download content type (used for both downloads and videos)
        var downloadFields = CreateDownloadFields();
        var downloadType = new StaticSitefinityType
        {
            Id = Guid.Parse("9315C03B-D20B-4843-A226-4CD975F8393D"),
            DisplayName = "Download File",
            ClassName = "DownloadFile",
            Namespace = "Migration.Toolkit.Media",
            Fields = downloadFields,
            LastModified = DateTime.Now
        };
        mediaTypes.Add(downloadType);

        // Video content type removed - videos now use DownloadFile content type
        // but still maintain /videos folder structure in ContentFolderManager

        return mediaTypes;
    }

    private static List<Field> CreateImageFields() =>
    [
        new()
        {
            Id = Guid.Parse("e477a59e-1df6-4e2f-9986-20ab37342540"),
            Name = "SelectedImage",
            Title = "Image",
            ColumnName = "SelectedImage",
            WidgetTypeName = "Kentico.Administration.ContentItemAssetUploader",
            IsRequired = false,
            DBType = "contentitemasset",
            FileExtensions = "jpg;jpeg;png;webp;bmp;svg;"
        },
        new()
        {
            Id = Guid.Parse("0c933519-4bc4-4cc1-8eb5-c3f685c7ab53"),
            Name = "ImageAltText",
            Title = "Alt text",
            ColumnName = "ImageAltText",
            WidgetTypeName = "Kentico.Administration.TextInput",
            IsRequired = false,
            DBType = "text",
            DBLength = "200",
            InstructionalText = "Leave blank unless image helps users understand content"
        }
    ];

    private static List<Field> CreateDownloadFields() =>
    [
        new()
        {
            Id = Guid.Parse("141d77fc-2e06-49eb-b14c-2ff58f5ce730"),
            Name = "SelectedFile",
            Title = "Selected File",
            ColumnName = "SelectedFile",
            WidgetTypeName = "Kentico.Administration.ContentItemAssetUploader",
            IsRequired = false,
            DBType = "contentitemasset",
            FileExtensions = "wav;mp3;mp4;mpg;mpeg;avi;webm;wmv;txt;pdf;docx;pptx;xlsx",
            InstructionalText = "Use this type for downloadable files (use the Image type for all image formats.)"
        }
        ,
        new()
        {
            Id = Guid.Parse("add02e6b-b70b-43fb-9a84-553d365ddecc"),
            Name = "ListingItemTitle",
            Title = "Listing title",
            ColumnName = "ListingItemTitle",
            WidgetTypeName = "Kentico.Administration.TextInput",
            IsRequired = false,
            DBType = "text",
            DBLength = "256"
        }
    ];

    public IEnumerable<SitefinityType> GetDynamicModuleTypes()
    {
        string dynamicModulesPath = configuration.SitefinityModuleDeploymentFolderPath + "\\Dynamic modules";

        if (!Path.IsPathRooted(configuration.SitefinityModuleDeploymentFolderPath))
        {
            dynamicModulesPath = Environment.CurrentDirectory + configuration.SitefinityModuleDeploymentFolderPath + "\\Dynamic modules";
        }

        if (string.IsNullOrEmpty(dynamicModulesPath) || !Directory.Exists(dynamicModulesPath))
        {
            logger.LogError("Sitefinity module deployment folder does not exist. {DynamicModulesPath}", dynamicModulesPath);
            return [];
        }

        var dynamicTypes = new List<DynamicModuleType>();

        foreach (string path in Directory.EnumerateFiles(dynamicModulesPath, "*.sf", SearchOption.AllDirectories)
                                            .Where(path => !Array.Exists(excludedFileNames, e => e.Equals(Path.GetFileName(path)))))
        {
            string fileContents = File.ReadAllText(path);

            if (string.IsNullOrEmpty(fileContents))
            {
                logger.LogWarning("File {Path} is empty.", path);
                continue;
            }

            var module = JsonSerializer.Deserialize<Module>(fileContents);

            if (module == null)
            {
                logger.LogWarning("File {Path} is not a valid module file.", path);
                continue;
            }

            if (module.Types == null || module.Types.Count == 0)
            {
                logger.LogWarning("Module {ModuleName} does not contain any types.", module.Name);
                continue;
            }


            if (module.Name == "ELFAEvents")
            {
                foreach (var type in module.Types)
                {
                    if (type.Name == "ElfaEvent" && type.Fields != null)
                    {
                        type.Fields.Add(new Field
                        {
                            Id = Guid.Parse("9B96237A-A336-4668-A681-DD47D00581F0"),
                            Name = "Programs",
                            Title = "Programs",
                            ColumnName = "Programs",
                            WidgetTypeName = "Telerik.Sitefinity.Web.UI.Fields.RelatedProgramsField",
                            IsRequired = false,
                            RelatedDataType = "Telerik.Sitefinity.DynamicTypes.Model.ELFAEvents.Program",

                        });
                    }
                }
            }

            if (module.Name is "State Compendium")
            {
                foreach (var type in module.Types)
                {
                    if ((type.Name == "CompendiumIssue" || type.Name == "TaxManualItem") && type.Fields != null)
                    {
                        type.Fields.Add(new Field
                        {
                            Id = Guid.Parse("7776237A-A336-4668-A681-DD47D00581F0"),
                            Name = "State",
                            Title = "State",
                            ColumnName = "State",
                            WidgetTypeName = "Telerik.Sitefinity.Web.UI.Fields.StateTaxonomyField",
                            IsRequired = false
                        });
                    }
                }
            }

            dynamicTypes.AddRange(module.Types);
        }

        return dynamicTypes;
    }

    public IEnumerable<SitefinityType> GetSitefinityTypes()
    {
        string deploymentFolderPath = configuration.SitefinityModuleDeploymentFolderPath;

        if (!Path.IsPathRooted(configuration.SitefinityModuleDeploymentFolderPath))
        {
            deploymentFolderPath = Environment.CurrentDirectory + configuration.SitefinityModuleDeploymentFolderPath;
        }

        if (string.IsNullOrEmpty(deploymentFolderPath) || !Directory.Exists(deploymentFolderPath))
        {
            logger.LogError("Sitefinity module deployment folder does not exist. {DeploymentFolderPath}", deploymentFolderPath);
            return [];
        }

        var staticSitefinityTypes = GetStaticSitefinityTypes();

        var sitefinityTypes = new List<SitefinityType>();

        foreach (string sitefinityPath in sitefinityTypeDirectories)
        {
            foreach (string path in Directory.EnumerateFiles($"{deploymentFolderPath}\\{sitefinityPath}", "*.sf", SearchOption.AllDirectories)
                                            .Where(path => !Array.Exists(excludedFileNames, e => e.Equals(Path.GetFileName(path)))))
            {
                string fileContents = File.ReadAllText(path);

                if (string.IsNullOrEmpty(fileContents))
                {
                    logger.LogWarning("File {Path} is empty.", path);
                    continue;
                }

                var types = JsonSerializer.Deserialize<IEnumerable<StaticSitefinityType>>(fileContents);

                if (types == null)
                {
                    logger.LogWarning("File {Path} is not a valid type file.", path);
                    continue;
                }

                sitefinityTypes.AddRange(types);
            }
        }

        foreach (var sitefinityType in sitefinityTypes)
        {
            var existingType = staticSitefinityTypes.Find(x => x.Name == sitefinityType.Name);

            if (existingType != null && existingType.Fields != null && sitefinityType.Fields != null)
            {
                existingType.Fields.AddRange(sitefinityType.Fields);
            }
        }

        return staticSitefinityTypes;
    }

    private List<StaticSitefinityType> GetStaticSitefinityTypes()
    {
        if (!Directory.Exists(staticSitefinityTypesDirectory))
        {
            logger.LogInformation("Static Sitefinity types folder does not exist. {StaticSitefinityTypesDirectory}", staticSitefinityTypesDirectory);
            return [];
        }

        var staticTypes = new List<StaticSitefinityType>();

        foreach (string path in Directory.EnumerateFiles(staticSitefinityTypesDirectory, "*.json", SearchOption.AllDirectories))
        {
            string fileContents = File.ReadAllText(path);

            if (string.IsNullOrEmpty(fileContents))
            {
                logger.LogWarning("File {Path} is empty.", path);
                continue;
            }

            var type = JsonSerializer.Deserialize<IEnumerable<StaticSitefinityType>>(fileContents);

            if (type == null)
            {
                logger.LogWarning("File {Path} is not a valid static sitefinity file.", path);
                continue;
            }

            staticTypes.AddRange(type);
        }

        return staticTypes;
    }
}
