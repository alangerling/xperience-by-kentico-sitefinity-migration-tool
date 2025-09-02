using CMS.ContentEngine;

using Kentico.Xperience.UMT.Model;
using Kentico.Xperience.UMT.Services;

using Microsoft.Extensions.Logging;

using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Configuration;
using Migration.Toolkit.Sitefinity.Core.Adapters;
using Migration.Toolkit.Sitefinity.Core.Helpers;
using Migration.Toolkit.Sitefinity.Core.Services;
using Migration.Toolkit.Sitefinity.Model;

using Progress.Sitefinity.RestSdk.Dto;

namespace Migration.Toolkit.Sitefinity.Services
{
    internal class ContentItemImportService(IImportService kenticoImportService,
                                            IContentLanguageImportService contentLanguageImportService,
                                            IChannelImportService channelImportService,
                                            IDataClassImportService dataClassImportService,
                                            IMediaImportService mediaImportService,
                                            IUserImportService userImportService,
                                            SitefinityImportConfiguration configuration,
                                            ContentFolderManager folderManager,
                                            IWebPageImportService webPageImportService,
                                            IContentProvider contentProvider,
                                            ITypeProvider typeProvider,
                                            IContentHelper contentHelper,
                                            SitefinityImportConfiguration importConfiguration,
                                            ILogger<ContentItemImportService> logger,
                                            IUmtAdapterWithDependencies<ContentItem, ContentDependencies, ContentItemSimplifiedModel> adapter,
                                            IContentFolderImportService contentFolderImportService,
                                            IExistingContentTypeMappingService existingContentTypeMappingService) : IContentItemImportService
    {
        public IEnumerable<ContentItemSimplifiedModel> Get(ContentDependencies dependenciesModel)
        {
            var typeDefinitions = new List<SitefinityTypeDefinition>();

            foreach (var dataClassGuid in dependenciesModel.DataClasses.Keys)
            {
                var dataClass = dependenciesModel.DataClasses[dataClassGuid];
                var types = typeProvider.GetAllTypes().Where(type => Array.Exists(Constants.ForcedWebsiteTypes, x => !x.Equals(type.Name)));

                // First try to find by DataClassGuid (for original Sitefinity types)
                var type = types.FirstOrDefault(x => x.Id == dataClassGuid);

                // If not found, this might be an existing Kentico content type
                // We need to find the original Sitefinity type that maps to this Kentico type
                if (type == null && !string.IsNullOrEmpty(dataClass.ClassName))
                {
                    logger.LogDebug("Could not find Sitefinity type for DataClass GUID {DataClassGuid} (ClassName: {ClassName}). Attempting reverse lookup for existing content type.",
                        dataClassGuid, dataClass.ClassName);

                    // Get all Sitefinity types that should map to existing Kentico content types
                    var allSitefinityTypes = types.ToList();

                    // Try to find a Sitefinity type by checking if any of them would map to this Kentico class
                    foreach (var sitefinityType in allSitefinityTypes)
                    {
                        if (sitefinityType.Name == null || sitefinityType.ClassNamespace == null)
                        {
                            continue;
                        }

                        // Check if this Sitefinity type would map to the current Kentico class
                        string mappedClassName = contentHelper.GetMappedClassName(sitefinityType.Name, $"{sitefinityType.ClassNamespace}.{sitefinityType.Name}");

                        if (!string.IsNullOrEmpty(mappedClassName) && mappedClassName.Equals(dataClass.ClassName, StringComparison.OrdinalIgnoreCase))
                        {
                            logger.LogDebug("Found Sitefinity type '{SitefinityType}' that maps to existing Kentico class '{KenticoClass}'",
                                sitefinityType.Name, dataClass.ClassName);
                            type = sitefinityType;
                            break;
                        }
                    }
                }

                if (type == null)
                {
                    logger.LogWarning("No type found for dataclass with ClassGuid of {DataClassGuid}. Cannot get items based on data class: {DataClassName}", dataClassGuid, dataClass.ClassName);
                    continue;
                }

                if (type.ClassNamespace == null || type.Name == null)
                {
                    continue;
                }

                typeDefinitions.Add(new SitefinityTypeDefinition
                {
                    SitefinityTypeNameSpace = type.ClassNamespace,
                    SitefinityTypeName = type.Name,
                    DataClassGuid = dataClassGuid, // Keep the original DataClassGuid for the content items to reference
                });
            }

            var channel = contentHelper.GetCurrentChannel(dependenciesModel.Channels.Values);
            var currentSite = contentHelper.GetCurrentSite();

            if (channel == null || currentSite == null)
            {
                logger.LogWarning("Channel/Site not found. Cannot import content items.");
                return [];
            }

            var detailPageConfigs = importConfiguration.PageContentTypes?.Where(x => x.PageTemplateType == PageTemplateType.Detail);

            var contentItems = contentProvider.GetContentItems(typeDefinitions, currentSite.SystemCultures).OrderByDescending(x => (detailPageConfigs?.Any(z => z.TypeName.Equals(x.TypeName)) ?? false) ? x.TypeName : "");

            // Filter content items to only include those created by backend users (exclude member submissions)
            var filteredContentItems = contentItems.Where(item =>
            {
                // Get admin GUID from configuration or use default
                var adminGuid = !string.IsNullOrEmpty(configuration.SitefinityAdminUserGuid)
                    ? Guid.Parse(configuration.SitefinityAdminUserGuid)
                    : Guid.Parse("6415B8CE-8072-4BCD-8E48-9D7178B826B7");

                // Check if the owner exists in the users dependencies (only backend users are imported) or is admin
                bool isBackendUser = dependenciesModel.Users.ContainsKey(item.Owner) || item.Owner == adminGuid;

                // Check if Event has Status=2 (published)
                bool isPublished = HasPublishedStatus(item);

                // Apply backend user filtering for NewsItem content type
                if (string.Equals(item.TypeName, "NewsItem", StringComparison.OrdinalIgnoreCase))
                {
                    if (!isPublished)
                    {
                        logger.LogInformation("Excluding NewsItem '{Title}' (ID: {Id}) - not published (Status != 2)",
                            item.Title, item.Id);
                        return false;
                    }

                    // Apply ELFA business rule filtering for NewsItems
                    if (!IsElfaNewsItem(item))
                    {
                        logger.LogInformation("Excluding NewsItem '{Title}' (ID: {Id}) - does not meet ELFA business criteria",
                            item.Title, item.Id);
                        return false;
                    }

                    if (!isBackendUser)
                    {
                        logger.LogInformation("Excluding {ContentType} '{Title}' (ID: {Id}) - submitted by member/frontend user (Owner: {Owner})",
                            item.TypeName, item.Title, item.Id, item.Owner);
                        return false;
                    }
                }

                // Additional filtering for Event content type by Status=2 (published)
                if (string.Equals(item.TypeName, "Event", StringComparison.OrdinalIgnoreCase))
                {
                    if (!isPublished)
                    {
                        logger.LogInformation("Excluding Event '{Title}' (ID: {Id}) - not published (Status != 2)",
                            item.Title, item.Id);
                        return false;
                    }
                }

                // For all other content types, include the item regardless of backend user status
                return true;
            });

            return adapter.Adapt(filteredContentItems, dependenciesModel);
        }

        /// <summary>
        /// Checks if a content item has published status (Status=2)
        /// </summary>
        /// <param name="item">The content item to check</param>
        /// <returns>True if the item has Status=2 (published), false otherwise</returns>
        private static bool HasPublishedStatus(ContentItem item)
        {
            try
            {
                // Try to get Status field from the content item
                if (item is SdkItem sdkItem)
                {
                    if (sdkItem.TryGetValue("Status", out int statusValue))
                    {
                        return statusValue == 2; // Published status
                    }

                    if (sdkItem.TryGetValue("Status", out string? statusString) &&
                        int.TryParse(statusString, out int parsedStatus))
                    {
                        return parsedStatus == 2; // Published status
                    }
                }

                // If no Status field found, default to include (assume published)
                return true;
            }
            catch
            {
                // If any error occurs, default to include (assume published)
                return true;
            }
        }

        /// <summary>
        /// Determines if a NewsItem meets ELFA filtering criteria based on organization and email rules.
        /// Equivalent to the SQL WHERE clause filtering logic:
        /// 
        /// WHERE status = 2 
        /// AND (organization IS NULL OR organization = '' OR organization = 'ELFA' 
        ///      OR organization = 'Equipment Leasing & Finance Magazine' 
        ///      OR organization = 'Equipment Leasing & Finance Foundation')
        /// AND (email IS NULL OR email = '' OR email LIKE '%elfaonline.org%' 
        ///      OR email LIKE '%leasefoundation.org%' OR email LIKE '%equipmentfinanceadvantage.org%')
        /// </summary>
        /// <param name="newsItem">The NewsItem to evaluate</param>
        /// <returns>True if the item should be included, false otherwise</returns>
        private bool IsElfaNewsItem(ContentItem newsItem)
        {
            // Get organization value - corresponds to SQL: organization column
            string? organization = newsItem.GetValue<string>("Organization")?.Trim();

            // Get email value - corresponds to SQL: email column  
            string? email = newsItem.GetValue<string>("Email")?.Trim();

            // Check organization criteria:
            // (organization IS NULL OR organization = '' OR organization = 'ELFA' 
            //  OR organization = 'Equipment Leasing & Finance Magazine' 
            //  OR organization = 'Equipment Leasing & Finance Foundation')
            bool organizationMatches = string.IsNullOrWhiteSpace(organization) ||
                                     organization.Equals("ELFA", StringComparison.OrdinalIgnoreCase) ||
                                     organization.Equals("Equipment Leasing & Finance Magazine", StringComparison.OrdinalIgnoreCase) ||
                                     organization.Equals("Equipment Leasing & Finance Foundation", StringComparison.OrdinalIgnoreCase);

            // Check email criteria:
            // (email IS NULL OR email = '' OR email LIKE '%elfaonline.org%' 
            //  OR email LIKE '%leasefoundation.org%' OR email LIKE '%equipmentfinanceadvantage.org%')
            bool emailMatches = string.IsNullOrWhiteSpace(email) ||
                              email.Contains("elfaonline.org", StringComparison.OrdinalIgnoreCase) ||
                              email.Contains("leasefoundation.org", StringComparison.OrdinalIgnoreCase) ||
                              email.Contains("equipmentfinanceadvantage.org", StringComparison.OrdinalIgnoreCase);

            // Note: The Status = 2 filter is handled by the REST SDK as it only retrieves published content

            bool passes = organizationMatches && emailMatches;

            if (!passes)
            {
                logger.LogTrace("NewsItem {ItemId} filtered out. Organization: '{Organization}' (matches: {OrgMatches}), Email: '{Email}' (matches: {EmailMatches})",
                    newsItem.Id, organization ?? "null", organizationMatches, email ?? "null", emailMatches);
            }

            return passes;
        }
        public SitefinityImportResult<ContentItemSimplifiedModel> StartImport(ImportStateObserver observer)
        {
            var languages = contentLanguageImportService.StartImport(observer);
            observer.ImportCompletedTask.Wait();

            var channelDependencies = new ChannelDependencies
            {
                ContentLanguages = languages.ImportedModels
            };
            var channels = channelImportService.StartImportWithDependencies(observer, channelDependencies);
            observer.ImportCompletedTask.Wait();

            var users = userImportService.StartImport(observer);
            observer.ImportCompletedTask.Wait();

            var dataClassDependencies = new DataClassDependencies
            {
                Channels = channels.ImportedModels.Values.OfType<ChannelModel>().ToDictionary(x => x.ChannelGUID)
            };
            var dataClassesResult = dataClassImportService.StartImportWithDependencies(observer, dataClassDependencies);
            observer.ImportCompletedTask.Wait();

            var mediaFiles = mediaImportService.StartImport(observer);
            observer.ImportCompletedTask.Wait();

            // Fix: Get DataClasses dictionary from SitefinityImportResult<DataClassModel>
            var dataClasses = dataClassesResult.ImportedModels;

            var contentFoldersByClassName = new Dictionary<Guid, ContentFolderModel>();

            var rootFolder = ContentFolderInfo.Provider.GetRootAsync(configuration.KenticoDefaultWorkspaceName).GetAwaiter().GetResult();

            // Create folders for reusable data classes coming from import
            foreach (var dataClass in dataClasses.Values.OfType<DataClassModel>())
            {
                if (!string.IsNullOrWhiteSpace(dataClass.ClassName) && dataClass.ClassGUID != null && (dataClass.ClassContentTypeType == "Reusable" || dataClass.ClassName == configuration.SitefinityCodeNamePrefix + ".Programs"))
                {
                    string folderName = "";
                    if (!string.IsNullOrWhiteSpace(dataClass.ClassName))
                    {
                        string[] split = dataClass.ClassName.Split('.', 2);
                        folderName = split.Length > 1 && !string.IsNullOrWhiteSpace(split[1])
                            ? split[1]
                            : dataClass.ClassName;
                    }
                    else
                    {
                        folderName = dataClass.ClassName;
                    }

                    var contentFolder = new ContentFolderModel
                    {
                        ContentFolderGUID = dataClass.ClassGUID,
                        ContentFolderName = folderName,
                        ContentFolderDisplayName = folderName,
                        ContentFolderTreePath = $"/{folderName}",
                        ContentFolderParentFolderGUID = null
                    };

                    contentFoldersByClassName.Add(dataClass.ClassGUID ?? rootFolder.ContentFolderGUID, contentFolder);
                }
            }

            // Ensure folders for built-in Kentico reusable types that may not be part of imported data classes
            // Organization (Elfa.Organization) and EventProgram (Elfa.EventProgram)
            string[] builtinReusableSitefinityTypes = new[] { "MagazineSponsor", "Program", "TaxManualItem" };
            foreach (string sfType in builtinReusableSitefinityTypes)
            {
                var existingType = existingContentTypeMappingService.GetExistingContentType(sfType);
                if (existingType == null)
                {
                    continue;
                }

                if (!string.Equals(existingType.ClassContentTypeType, "Reusable", StringComparison.Ordinal))
                {
                    continue;
                }

                var classGuid = existingType.ClassGUID;
                if (contentFoldersByClassName.ContainsKey(classGuid))
                {
                    continue;
                }

                string className = existingType.ClassName;
                string folderNameFromClass = className;
                string[] classSplit = className.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
                if (classSplit.Length > 1 && !string.IsNullOrWhiteSpace(classSplit[1]))
                {
                    folderNameFromClass = classSplit[1];
                }

                var builtinFolder = new ContentFolderModel
                {
                    ContentFolderGUID = classGuid,
                    ContentFolderName = folderNameFromClass,
                    ContentFolderDisplayName = folderNameFromClass,
                    ContentFolderTreePath = $"/{folderNameFromClass}",
                    ContentFolderParentFolderGUID = null
                };

                contentFoldersByClassName.Add(classGuid, builtinFolder);
            }

            var dependencies = new ContentDependencies
            {
                MediaFiles = mediaFiles.ImportedModels,
                Users = users.ImportedModels,
                DataClasses = dataClasses.Values.OfType<DataClassModel>().ToDictionary(x => x.ClassGUID),
                Channels = channels.ImportedModels.Values.OfType<ChannelModel>().ToDictionary(x => x.ChannelGUID),
                ContentLanguages = languages.ImportedModels,
                ContentFolders = contentFoldersByClassName
            };

            // Pre-create subfolders for Program items under EventProgram based on their URL structure
            var currentSite = contentHelper.GetCurrentSite();
            if (currentSite != null)
            {
                var programType = typeProvider.GetAllTypes().FirstOrDefault(t => t.Name != null && t.Name.Equals("Program", StringComparison.OrdinalIgnoreCase));
                if (programType != null)
                {
                    var programTypeDefs = new[]
                    {
                        new SitefinityTypeDefinition
                        {
                            SitefinityTypeNameSpace = programType.ClassNamespace!,
                            SitefinityTypeName = programType.Name!,
                            DataClassGuid = programType.Id
                        }
                    };

                    foreach (var programItem in contentProvider.GetProgramsContentItems(programTypeDefs, currentSite.SystemCultures))
                    {
                        string subfolderPath = GetProgramSubfolderPathFromUrl(programItem.Url);
                        if (!string.IsNullOrWhiteSpace(subfolderPath))
                        {
                            // Ensure root EventProgram exists in dependencies before creating subfolders
                            folderManager.GetOrCreateContentTypeFolderPath("EventProgram", subfolderPath, dependencies);
                        }
                    }
                }

                // Pre-create subfolders for TaxManualItem items under "Tax Manual Items/[State]"
                var taxManualType = typeProvider.GetAllTypes().FirstOrDefault(t => t.Name != null && t.Name.Equals("TaxManualItem", StringComparison.OrdinalIgnoreCase));
                if (taxManualType != null)
                {
                    var taxTypeDefs = new[]
                    {
                        new SitefinityTypeDefinition
                        {
                            SitefinityTypeNameSpace = taxManualType.ClassNamespace!,
                            SitefinityTypeName = taxManualType.Name!,
                            DataClassGuid = taxManualType.Id
                        }
                    };

                    foreach (var taxItem in contentProvider.GetContentItems(taxTypeDefs, currentSite.SystemCultures).Where(ci => string.Equals(ci.TypeName, "TaxManualItem", StringComparison.OrdinalIgnoreCase)))
                    {
                        string stateFolder = GetFirstPathSegment(taxItem.ItemDefaultUrl ?? taxItem.Url);
                        if (!string.IsNullOrWhiteSpace(stateFolder))
                        {
                            folderManager.GetOrCreateContentTypeFolderPath("Tax Manual Items", stateFolder, dependencies);
                        }
                    }
                }
            }

            // Import content folders before importing content items
            var folderDependencies = new ContentFolderDependencies { ContentFolders = dependencies.ContentFolders };
            folderManager.AddFolders(dependencies.ContentFolders);
            contentFolderImportService.StartImportWithDependencies(observer, folderDependencies);
            observer.ImportCompletedTask.Wait();

            var webpages = webPageImportService.StartImportWithDependencies(observer, dependencies);
            observer.ImportCompletedTask.Wait();

            dependencies.WebPages = webpages.ImportedModels;

            var contentItems = Get(dependencies).OrderBy(x => x.PageData == null ? "" : x.PageData.TreePath);

            return new SitefinityImportResult<ContentItemSimplifiedModel>
            {
                ImportedModels = contentItems.ToDictionary(x => x.ContentItemGUID),
                Observer = kenticoImportService.StartImport(contentItems, observer)
            };
        }
        public SitefinityImportResult<ContentItemSimplifiedModel> StartImportWithDependencies(ImportStateObserver observer, ContentDependencies dependenciesModel)
        {
            var contentItems = Get(dependenciesModel);

            return new SitefinityImportResult<ContentItemSimplifiedModel>
            {
                ImportedModels = contentItems.ToDictionary(x => x.ContentItemGUID),
                Observer = kenticoImportService.StartImport(contentItems, observer)
            };
        }

        private static string GetProgramSubfolderPathFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string relative = url.Trim();
            if (Uri.TryCreate(relative, UriKind.Absolute, out var absolute))
            {
                relative = absolute.PathAndQuery;
            }

            relative = relative.Trim('/');
            if (string.IsNullOrEmpty(relative))
            {
                return string.Empty;
            }

            int lastSlash = relative.LastIndexOf('/');
            return lastSlash > 0 ? relative[..lastSlash] : string.Empty;
        }

        private static string GetFirstPathSegment(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            string relative = url.Trim();
            if (Uri.TryCreate(relative, UriKind.Absolute, out var abs))
            {
                relative = abs.PathAndQuery;
            }

            string[] segments = relative.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return string.Empty;
            }

            // Return the first segment (e.g., state code/name)
            return segments[0];
        }
    }
}
