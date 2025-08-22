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
    internal class WebPageImportService(
        IImportService kenticoImportService,
        IContentLanguageImportService contentLanguageImportService,
        IChannelImportService channelImportService,
        IDataClassImportService dataClassImportService,
        IMediaImportService mediaImportService,
        IUserImportService userImportService,
        IContentProvider contentProvider,
        ISiteProvider siteProvider,
        IContentHelper contentHelper,
        ILogger<WebPageImportService> logger,
        IUmtAdapterWithDependencies<Page, ContentDependencies, ContentItemSimplifiedModel> adapter,
        SitefinityImportConfiguration importConfiguration,
        ITypeProvider typeProvider) : IWebPageImportService
    {
        public IEnumerable<ContentItemSimplifiedModel> Get(ContentDependencies dependenciesModel)
        {
            var channel = contentHelper.GetCurrentChannel(dependenciesModel.Channels.Values);
            if (channel == null)
            {
                logger.LogWarning("Channel not found. Cannot import content items.");
                return [];
            }

            var currentSite = siteProvider.GetSites().First(x => x.Id.Equals(channel.ChannelGUID));

            // Get required page paths from config (roots)
            var requiredPaths = importConfiguration.PageContentTypes?.Select(x => x.PageRootPath).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Build additional hierarchical paths based on item ItemDefaultUrl for selected Listing page types
            var listingConfigs = importConfiguration.PageContentTypes?.Where(x => x.PageTemplateType == PageTemplateType.Listing).ToList() ?? [];

            // Only create hierarchies for these types (easily extend by adding more names)
            var hierarchyTypes = new HashSet<string>(new[] { "NewsItem", "Event" }, StringComparer.OrdinalIgnoreCase);
            listingConfigs = listingConfigs.Where(cfg => hierarchyTypes.Contains(cfg.TypeName)).ToList();

            if (listingConfigs.Count > 0)
            {
                // Build type definitions for selected listing types
                var allTypes = typeProvider.GetAllTypes();
                var typeDefs = new List<SitefinityTypeDefinition>();
                var typeToRootMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var cfg in listingConfigs)
                {
                    var sfType = allTypes.FirstOrDefault(t => t.Name != null && t.Name.Equals(cfg.TypeName, StringComparison.OrdinalIgnoreCase));
                    if (sfType?.Name == null || sfType.ClassNamespace == null)
                    {
                        continue;
                    }
                    typeDefs.Add(new SitefinityTypeDefinition
                    {
                        SitefinityTypeNameSpace = sfType.ClassNamespace,
                        SitefinityTypeName = sfType.Name,
                        DataClassGuid = sfType.Id
                    });
                    typeToRootMap[sfType.Name] = cfg.PageRootPath;
                }

                if (typeDefs.Count > 0)
                {
                    var items = contentProvider.GetContentItems(typeDefs, currentSite.SystemCultures);

                    // Filter NewsItems using the same rules as content import (published, ELFA rules, backend user)
                    var adminGuid = !string.IsNullOrEmpty(importConfiguration.SitefinityAdminUserGuid)
                        ? Guid.Parse(importConfiguration.SitefinityAdminUserGuid)
                        : Guid.Parse("6415B8CE-8072-4BCD-8E48-9D7178B826B7");

                    var filtered = items.Where(item =>
                    {
                        if (string.Equals(item.TypeName, "NewsItem", StringComparison.OrdinalIgnoreCase))
                        {
                            bool isPublished = HasPublishedStatus(item);
                            if (!isPublished)
                            {
                                return false;
                            }

                            if (!IsElfaNewsItem(item))
                            {
                                return false;
                            }

                            bool isBackendUser = dependenciesModel.Users.ContainsKey(item.Owner) || item.Owner == adminGuid;
                            if (!isBackendUser)
                            {
                                return false;
                            }
                        }

                        return true;
                    });

                    foreach (var item in filtered)
                    {
                        if (string.IsNullOrWhiteSpace(item.ItemDefaultUrl) || string.IsNullOrWhiteSpace(item.TypeName))
                        {
                            continue;
                        }

                        if (!typeToRootMap.TryGetValue(item.TypeName, out string? rootPath) || string.IsNullOrWhiteSpace(rootPath))
                        {
                            continue;
                        }

                        // Normalize URL and strip unwanted segments (e.g., default-calendar) before building hierarchy
                        string normalizedUrl = contentHelper.GetRelativeUrl(item.ItemDefaultUrl ?? string.Empty);

                        // Extract folder segments (exclude last segment which is the page slug)
                        string[] segments = normalizedUrl.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (segments.Length <= 1)
                        {
                            continue; // no hierarchy
                        }

                        // Build incremental paths under the root
                        string current = rootPath.TrimEnd('/');
                        for (int i = 0; i < segments.Length - 1; i++)
                        {
                            current += "/" + segments[i];
                            requiredPaths.Add(current);
                        }
                    }
                }
            }

            var pages = requiredPaths.Any()
                ? contentProvider.GetPages(currentSite.SystemCultures, requiredPaths)
                : contentProvider.GetPages(currentSite.SystemCultures);

            return adapter.Adapt(pages, dependenciesModel);
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

            var dataClasses = dataClassImportService.StartImportWithDependencies(observer, dataClassDependencies);

            observer.ImportCompletedTask.Wait();

            var mediaFiles = mediaImportService.StartImport(observer);

            observer.ImportCompletedTask.Wait();

            var dependencies = new ContentDependencies
            {
                MediaFiles = mediaFiles.ImportedModels,
                Users = users.ImportedModels,
                DataClasses = dataClasses.ImportedModels.Values.OfType<DataClassModel>().ToDictionary(x => x.ClassGUID),
                Channels = channels.ImportedModels.Values.OfType<ChannelModel>().ToDictionary(x => x.ChannelGUID),
                ContentLanguages = languages.ImportedModels
            };

            return Import(observer, dependencies);
        }

        public SitefinityImportResult<ContentItemSimplifiedModel> StartImportWithDependencies(ImportStateObserver observer, ContentDependencies dependenciesModel) => Import(observer, dependenciesModel);

        private SitefinityImportResult<ContentItemSimplifiedModel> Import(ImportStateObserver observer, ContentDependencies dependencies)
        {
            var pages = Get(dependencies);

            return new SitefinityImportResult<ContentItemSimplifiedModel>
            {
                ImportedModels = pages.ToDictionary(x => x.ContentItemGUID),
                Observer = kenticoImportService.StartImport(pages, observer)
            };
        }

        private static bool HasPublishedStatus(ContentItem item)
        {
            try
            {
                if (item is SdkItem sdkItem)
                {
                    if (sdkItem.TryGetValue("Status", out int statusValue))
                    {
                        return statusValue == 2;
                    }

                    if (sdkItem.TryGetValue("Status", out string? statusString) && int.TryParse(statusString, out int parsedStatus))
                    {
                        return parsedStatus == 2;
                    }
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        private bool IsElfaNewsItem(ContentItem newsItem)
        {
            string? organization = newsItem.GetValue<string>("Organization")?.Trim();
            string? email = newsItem.GetValue<string>("Email")?.Trim();

            bool organizationMatches = string.IsNullOrWhiteSpace(organization) ||
                                     organization.Equals("ELFA", StringComparison.OrdinalIgnoreCase) ||
                                     organization.Equals("Equipment Leasing & Finance Magazine", StringComparison.OrdinalIgnoreCase) ||
                                     organization.Equals("Equipment Leasing & Finance Foundation", StringComparison.OrdinalIgnoreCase);

            bool emailMatches = string.IsNullOrWhiteSpace(email) ||
                              email.Contains("elfaonline.org", StringComparison.OrdinalIgnoreCase) ||
                              email.Contains("leasefoundation.org", StringComparison.OrdinalIgnoreCase) ||
                              email.Contains("equipmentfinanceadvantage.org", StringComparison.OrdinalIgnoreCase);

            return organizationMatches && emailMatches;
        }
    }
}
