using CMS.ContentEngine;
using CMS.Helpers;

using Kentico.Xperience.UMT.Model;

using Microsoft.Extensions.Logging;

using Migration.Toolkit.Data.Configuration;
using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Abstractions;
using Migration.Toolkit.Sitefinity.Configuration;
using Migration.Toolkit.Sitefinity.Core.Helpers;
using Migration.Toolkit.Sitefinity.Model;
using Migration.Toolkit.Sitefinity.Services;

namespace Migration.Toolkit.Sitefinity.Adapters;
internal class ContentItemSimplifiedModelAdapter(ILogger<ContentItemSimplifiedModelAdapter> logger,
                                                 IContentHelper contentHelper,
                                                 IUserHelper userHelper,
                                                 SitefinityImportConfiguration configuration,
                                                 SitefinityDataConfiguration dataConfiguration,
                                                 ContentFolderManager contentFolderManager,
                                                 IExistingContentTypeMappingService existingContentTypeMappingService) : UmtAdapterBaseWithDependencies<ContentItem, ContentDependencies, ContentItemSimplifiedModel>(logger)
{
    private readonly Dictionary<Guid, ContentItemSimplifiedModel> detailContentItems = [];

    private readonly Dictionary<int, Guid> stateContentItemGuids = [];
    private readonly Dictionary<Guid, Guid> magazineIssueContentItemGuids = [];

    /// <summary>
    /// Maps CompendiumAuthor IDs to their corresponding state folder paths.
    /// This dictionary is populated when processing CompendiumIssue items that reference authors,
    /// and is used later when processing individual CompendiumAuthor items to place them
    /// in the correct state-specific folder structure.
    /// Key: Author ContentItem ID, Value: State folder path (e.g., "/compendium/texas")
    /// </summary>
    private readonly Dictionary<Guid, string> compendiumAuthorStateMapping = [];

    protected override ContentItemSimplifiedModel? AdaptInternal(ContentItem source, ContentDependencies dependenciesModel)
    {
        var rootFolder = ContentFolderInfo.Provider.GetRootAsync(configuration.KenticoDefaultWorkspaceName).GetAwaiter().GetResult();

        if (rootFolder == null)
        {
            logger.LogWarning("Content Hub root folder not found.");
            return default;
        }

        // Filter out CompendiumAuthor items that are not in the compendiumAuthorStateMapping
        if (source.TypeName == "CompendiumAuthor" && !compendiumAuthorStateMapping.ContainsKey(source.Id))
        {
            logger.LogInformation("Filtering out CompendiumAuthor {AuthorId} ({AuthorTitle}) - not referenced by any CompendiumIssue items.",
                source.Id, source.Title);
            return default;
        }

        // Check if there's an existing content type mapping for this Sitefinity type
        var existingContentType = existingContentTypeMappingService.GetExistingContentType(source.TypeName);

        if (existingContentType == null)
        {
            logger.LogDebug("No existing content type mapping found for Sitefinity type '{SitefinityType}'. Skipping content item {ItemId} ({ItemTitle}).",
                source.TypeName, source.Id, source.Title);
            return default;
        }

        // Use existing Kentico content type
        var targetDataClass = existingContentTypeMappingService.CreateDataClassModelFromExisting(existingContentType);
        string finalClassName = existingContentType.ClassName;

        logger.LogInformation("Using existing Kentico content type for Sitefinity type '{SitefinityType}': '{KenticoClass}' (GUID: {ClassGuid}) - ContentTypeType: {ContentTypeType}",
            source.TypeName, existingContentType.ClassName, existingContentType.ClassGUID, existingContentType.ClassContentTypeType);

        var users = dependenciesModel.Users;
        users.TryGetValue(ValidationHelper.GetGuid(source.Owner, Guid.Empty), out var createdByUser);
        var languageData = contentHelper.GetLanguageData(dependenciesModel, source, targetDataClass, createdByUser);

        if (targetDataClass.ClassContentTypeType == null)
        {
            logger.LogDebug("Content type type is null for {ItemId} ({ItemTitle}). Using AdaptReusable with root folder.", source.Id, source.Title);
            return AdaptReusable(source, languageData, rootFolder, finalClassName);
        }

        if (targetDataClass.ClassContentTypeType.Equals("Reusable"))
        {
            logger.LogDebug("Content type is Reusable for {ItemId} ({ItemTitle}). Using AdaptReusable.", source.Id, source.Title);

            // For Program items, place them into subfolders under EventProgram based on ItemDefaultUrl/Url
            if (string.Equals(source.TypeName, "Program", StringComparison.OrdinalIgnoreCase))
            {
                string? urlForFolder = source.ItemDefaultUrl ?? source.Url;
                string subfolderPath = GetProgramSubfolderPath(urlForFolder);
                var folderGuid = contentFolderManager.GetOrCreateContentTypeFolderPath("EventProgram", subfolderPath, dependenciesModel);

                return AdaptReusable(source, languageData, new ContentFolderInfo { ContentFolderGUID = folderGuid }, finalClassName);
            }

            return AdaptReusable(source, languageData, new ContentFolderInfo { ContentFolderGUID = targetDataClass.ClassGUID ?? rootFolder.ContentFolderGUID }, finalClassName);
        }

        if (targetDataClass.ClassContentTypeType.Equals("Website"))
        {
            logger.LogDebug("Content type is Website for {ItemId} ({ItemTitle}). Using AdaptPage.", source.Id, source.Title);
            return AdaptPage(source, languageData, dependenciesModel, finalClassName);
        }

        logger.LogDebug("Content type type '{ContentTypeType}' not recognized for {ItemId} ({ItemTitle}). Using AdaptReusable with root folder.", targetDataClass.ClassContentTypeType, source.Id, source.Title);
        return AdaptReusable(source, languageData, rootFolder, finalClassName);
    }

    private static string GetProgramSubfolderPath(string? itemUrl)
    {
        if (string.IsNullOrWhiteSpace(itemUrl))
        {
            return string.Empty;
        }

        string relative = itemUrl.Trim();
        if (Uri.TryCreate(relative, UriKind.Absolute, out var abs))
        {
            relative = abs.PathAndQuery;
        }

        relative = relative.Trim('/');

        if (string.IsNullOrEmpty(relative))
        {
            return string.Empty;
        }

        int lastSlash = relative.LastIndexOf('/');
        return lastSlash > 0 ? relative[..lastSlash] : string.Empty;
    }

    private ContentItemSimplifiedModel? AdaptPage(ContentItem source, IEnumerable<ContentItemLanguageData> languageData, ContentDependencies dependenciesModel, string finalClassName)
    {
        var channel = contentHelper.GetCurrentChannel(dependenciesModel.Channels.Values);

        if (channel == null)
        {
            logger.LogWarning("Channel not found for domain: {Domain}. Skipping content item {ContentItemDefaultUrl}.", dataConfiguration.SitefinitySiteDomain, source.UrlName);
            return default;
        }

        // Add validation for ChannelName
        if (string.IsNullOrWhiteSpace(channel.ChannelName))
        {
            logger.LogError("Channel found but ChannelName is null or empty for content item {ContentItemId} ({ContentItemTitle}). Channel GUID: {ChannelGuid}, Channel DisplayName: {ChannelDisplayName}. Skipping content item.",
                source.Id, source.Title, channel.ChannelGUID, channel.ChannelDisplayName);
            return default;
        }

        // Use exact match for TypeName instead of Contains
        var pageConfigs = configuration.PageContentTypes?.Where(x => source.TypeName != null && source.TypeName.Equals(x.TypeName, StringComparison.InvariantCultureIgnoreCase));

        if (pageConfigs == null || !pageConfigs.Any())
        {
            var parentGuid = ValidationHelper.GetGuid(source.ParentId, Guid.Empty);
            string treePath = source.ItemDefaultUrl;
            string? pagePath = null;

            if (detailContentItems.TryGetValue(parentGuid, out var detailContentItem))
            {
                parentGuid = ValidationHelper.GetGuid(detailContentItem.ContentItemGUID, Guid.Empty);
                treePath = (detailContentItem.PageData?.TreePath ?? string.Empty) + contentHelper.RemovePathSegmentsFromStart(source.ItemDefaultUrl ?? string.Empty, 2);
                pagePath = treePath;
            }

            var noPageConfigPageData = new PageDataModel
            {
                ItemOrder = null,
                PageUrls = contentHelper.GetPageUrls(dependenciesModel, source, pagePath: pagePath),
                PageGuid = source.Id,
                ParentGuid = parentGuid,
                TreePath = treePath
            };

            var noPageConfigPageContentItem = new ContentItemSimplifiedModel
            {
                ContentItemGUID = source.Id,
                ContentTypeName = finalClassName, // Use finalClassName instead of mappedClassName
                Name = contentHelper.GetName(source.Title, source.Id),
                LanguageData = languageData.ToList(),
                IsReusable = false,
                PageData = noPageConfigPageData,
                ChannelName = channel.ChannelName
            };

            return noPageConfigPageContentItem;
        }

        foreach (var pageConfig in pageConfigs)
        {
            string codeNamePrefix = configuration.SitefinityCodeNamePrefix;

            if (source.TypeName == $"State")
            {
                string[] segments = (source.ItemDefaultUrl ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
                string stateName = segments.Length > 0 ? segments[0].ToLowerInvariant() : string.Empty;
                stateContentItemGuids[stateName.GetHashCode()] = source.Id;
            }

            if (source.TypeName is "CompendiumIssue")
            {
                var authors = source.GetValue<IEnumerable<ContentItem>>("Authors");

                // Extract the state name from the CompendiumIssue's URL to map authors to state folders
                string[] segments = (source.ItemDefaultUrl ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
                string stateName = segments.Length > 0 ? segments[0].ToLowerInvariant() : string.Empty;
                string stateFolderPath = $"{pageConfig.PageRootPath}/{stateName}";

                // Map each author to the state folder path
                if (authors != null && !string.IsNullOrEmpty(stateName))
                {
                    foreach (var author in authors)
                    {
                        compendiumAuthorStateMapping[author.Id] = stateFolderPath;
                        logger.LogDebug("Mapped CompendiumAuthor {AuthorId} ({AuthorTitle}) to state folder path: {StateFolderPath}",
                            author.Id, author.Title, stateFolderPath);
                    }
                }
            }

            if (source.TypeName is "TaxManualItem" or "CompendiumIssue")
            {
                // Extract the state name as the first folder segment from ItemDefaultUrl
                string[] segments = (source.ItemDefaultUrl ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
                string stateName = segments.Length > 0 ? segments[0].ToLowerInvariant() : string.Empty;
                string taxItemsPath = $"{pageConfig.PageRootPath}/{stateName}";

                stateContentItemGuids.TryGetValue(stateName.GetHashCode(), out var parentGuid);

                var stateContentItemPageData = new PageDataModel
                {
                    ItemOrder = null,
                    PageUrls = contentHelper.GetPageUrls(dependenciesModel, source, rootPath: taxItemsPath),
                    PageGuid = source.Id,
                    ParentGuid = parentGuid,
                    TreePath = pageConfig.PageRootPath + (source.ItemDefaultUrl ?? string.Empty)
                };

                var stateContentItem = new ContentItemSimplifiedModel
                {
                    ContentItemGUID = source.Id,
                    ContentTypeName = finalClassName, // Use finalClassName instead of mappedClassName
                    Name = contentHelper.GetName(source.Title, source.Id),
                    LanguageData = languageData.ToList(),
                    IsReusable = false,
                    PageData = stateContentItemPageData,
                    ChannelName = channel.ChannelName
                };

                return stateContentItem;
            }
            // Special handling for CompendiumAuthor to use state folder mapping
            if (source.TypeName == "CompendiumAuthor")
            {
                // Check if we have a state folder mapping for this author
                if (compendiumAuthorStateMapping.TryGetValue(source.Id, out string? authorStateFolderPath))
                {
                    // Extract state name from the folder path for parent lookup
                    string[] pathSegments = authorStateFolderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    string stateName = pathSegments.Length > 1 ? pathSegments[^1].ToLowerInvariant() : string.Empty; // Get last segment (state name) and convert to lowercase

                    stateContentItemGuids.TryGetValue(stateName.GetHashCode(), out var parentGuid);

                    var authorStatePageData = new PageDataModel
                    {
                        ItemOrder = null,
                        PageUrls = contentHelper.GetPageUrls(dependenciesModel, source, rootPath: authorStateFolderPath),
                        PageGuid = source.Id,
                        ParentGuid = parentGuid,
                        TreePath = authorStateFolderPath + (source.ItemDefaultUrl ?? string.Empty)
                    };

                    var authorStateContentItem = new ContentItemSimplifiedModel
                    {
                        ContentItemGUID = source.Id,
                        ContentTypeName = finalClassName,
                        Name = contentHelper.GetName(source.Title, source.Id),
                        LanguageData = languageData.ToList(),
                        IsReusable = false,
                        PageData = authorStatePageData,
                        ChannelName = channel.ChannelName
                    };

                    logger.LogDebug("CompendiumAuthor {AuthorId} ({AuthorTitle}) placed in state folder: {StateFolderPath}",
                        source.Id, source.Title, authorStateFolderPath);

                    return authorStateContentItem;
                }
                else
                {
                    logger.LogWarning("No state folder mapping found for CompendiumAuthor {AuthorId} ({AuthorTitle}). Using default page config.",
                        source.Id, source.Title);
                }
            }

            // Special handling for Magazine content types to create parent-child relationships
            if (source.TypeName == "MagazineIssue")
            {
                // Store the MagazineIssue GUID for later use by MagazineArticle and MagazineAuthor
                // Use the source ID as the key since child items will reference it via ParentId
                magazineIssueContentItemGuids[source.Id] = source.Id;

                logger.LogDebug("Stored MagazineIssue '{MagazineIssueTitle}' with ID {MagazineIssueId}",
                    source.Title, source.Id);
            }

            if (source.TypeName is "MagazineArticle" or "MagazineAuthor")
            {
                // Find the parent MagazineIssue using the ParentId field
                var parentMagazineIssueGuid = GetParentMagazineIssueId(source);

                if (parentMagazineIssueGuid != Guid.Empty &&
                    magazineIssueContentItemGuids.ContainsKey(parentMagazineIssueGuid))
                {
                    logger.LogDebug("Found parent MagazineIssue with ID {ParentMagazineIssueId} for {ContentType} '{Title}'",
                        parentMagazineIssueGuid, source.TypeName, source.Title);
                }
                else
                {
                    logger.LogWarning("Could not find parent MagazineIssue with ID {ParentMagazineIssueId} for {ContentType} '{Title}'. Will use listing page as parent.",
                        parentMagazineIssueGuid, source.TypeName, source.Title);
                    parentMagazineIssueGuid = Guid.Empty; // Reset to ensure fallback behavior
                }

                // Create the child page data with the MagazineIssue as parent (if found)
                var magazineChildPageData = new PageDataModel
                {
                    ItemOrder = null,
                    PageUrls = contentHelper.GetPageUrls(dependenciesModel, source, pageConfig.PageRootPath),
                    PageGuid = source.Id,
                    ParentGuid = parentMagazineIssueGuid != Guid.Empty ? parentMagazineIssueGuid : (dependenciesModel.WebPages?.Values
                        .FirstOrDefault(x => x.PageData?.TreePath != null &&
                            x.PageData.TreePath.Equals(pageConfig.PageRootPath, StringComparison.OrdinalIgnoreCase))?.ContentItemGUID ?? Guid.Empty),
                    TreePath = pageConfig.PageRootPath + (source.ItemDefaultUrl ?? string.Empty)
                };

                var magazineChildContentItem = new ContentItemSimplifiedModel
                {
                    ContentItemGUID = source.Id,
                    ContentTypeName = finalClassName,
                    Name = contentHelper.GetName(source.Title, source.Id),
                    LanguageData = languageData.ToList(),
                    IsReusable = false,
                    PageData = magazineChildPageData,
                    ChannelName = channel.ChannelName
                };

                return magazineChildContentItem;
            }

            if (pageConfig.PageTemplateType == PageTemplateType.Listing)
            {
                // Try to find the most specific listing page based on folders in ItemDefaultUrl (e.g., yyyy/mm/dd for news)
                ContentItemSimplifiedModel? listingPage = null;
                string candidateFolderPath = ExtractDateFolderPathFromItemUrl(source.ItemDefaultUrl);
                if (!string.IsNullOrWhiteSpace(candidateFolderPath))
                {
                    string candidatePath = $"{pageConfig.PageRootPath.TrimEnd('/')}/{candidateFolderPath}";
                    listingPage = dependenciesModel.WebPages?.Values
                        .FirstOrDefault(x => x.PageData?.TreePath != null &&
                            x.PageData.TreePath.Equals(candidatePath, StringComparison.OrdinalIgnoreCase));
                }

                // Fallback to root listing page if no specific folder match found
                listingPage ??= dependenciesModel.WebPages?.Values
                    .FirstOrDefault(x => x.PageData?.TreePath != null &&
                        x.PageData.TreePath.Equals(pageConfig.PageRootPath, StringComparison.OrdinalIgnoreCase));

                if (listingPage == null)
                {
                    return default;
                }

                var listingChildPageData = new PageDataModel
                {
                    ItemOrder = null,
                    PageUrls = contentHelper.GetPageUrls(dependenciesModel, source, pageConfig.PageRootPath),
                    PageGuid = source.Id,
                    ParentGuid = listingPage.ContentItemGUID,
                    TreePath = pageConfig.PageRootPath + (source.ItemDefaultUrl ?? string.Empty)
                };

                // Special handling for NewsItem to add former URLs only (no TreePath override)
                if (source.TypeName == "NewsItem")
                {
                    const string newsListingRootPath = "/news-and-publications/industry-news";
                    var formerUrls = CreateNewsItemFormerUrls(source, newsListingRootPath, dependenciesModel);
                    listingChildPageData.PageFormerUrls = formerUrls;

                    logger.LogInformation("NewsItem {ItemId} ({ItemTitle}) created with {FormerUrlCount} former URLs under parent page {ParentGuid}, TreePath: {TreePath}",
                        source.Id, source.Title, formerUrls.Count, listingPage.ContentItemGUID, listingChildPageData.TreePath);
                }

                var listingChildPageContentItem = new ContentItemSimplifiedModel
                {
                    ContentItemGUID = source.Id,
                    ContentTypeName = finalClassName, // Use finalClassName instead of mappedClassName
                    Name = contentHelper.GetName(source.Title, source.Id),
                    LanguageData = languageData.ToList(),
                    IsReusable = false,
                    PageData = listingChildPageData,
                    ChannelName = channel.ChannelName
                };

                return listingChildPageContentItem;
            }

            if (pageConfig.PageTemplateType == PageTemplateType.Detail && (pageConfig.PageRootPath.Equals(source.Url) || (pageConfig.ItemUrlName != null && pageConfig.ItemUrlName.Equals(source.Url))))
            {
                var detailPage = dependenciesModel.WebPages?.Values.FirstOrDefault(x => (x.PageData?.TreePath?.Equals(pageConfig.PageRootPath) ?? false) || (x.PageData?.TreePath?.Equals(pageConfig.ItemUrlName) ?? false));

                if (detailPage == null)
                {
                    return default;
                }

                var parentDetailPage = dependenciesModel.WebPages?.Values.FirstOrDefault(x => x.PageData?.TreePath?.Equals(contentHelper.GetParentPath(detailPage.PageData?.TreePath)) ?? false);

                if (parentDetailPage == null)
                {
                    return default;
                }

                detailPage.ContentTypeName = finalClassName; // Use finalClassName instead of mappedClassName
                detailPage.LanguageData = languageData.ToList();

                detailContentItems.Add(source.Id, detailPage);

                return detailPage;
            }
        }

        var pageData = new PageDataModel
        {
            ItemOrder = null,
            PageUrls = contentHelper.GetPageUrls(dependenciesModel, source),
            PageGuid = source.Id,
            ParentGuid = ValidationHelper.GetGuid(source.ParentId, Guid.Empty),
            TreePath = source.ItemDefaultUrl
        };

        var pageContentItem = new ContentItemSimplifiedModel
        {
            ContentItemGUID = source.Id,
            ContentTypeName = finalClassName, // Use finalClassName instead of mappedClassName
            Name = contentHelper.GetName(source.Title, source.Id),
            LanguageData = languageData.ToList(),
            IsReusable = false,
            PageData = pageData,
            ChannelName = channel.ChannelName
        };

        return pageContentItem;
    }

    private ContentItemSimplifiedModel AdaptReusable(ContentItem source, IEnumerable<ContentItemLanguageData> languageData, ContentFolderInfo folder, string finalClassName) => new()
    {
        ContentItemGUID = source.Id,
        ContentTypeName = finalClassName, // Use finalClassName instead of mappedClassName
        Name = contentHelper.GetName(source.Title, source.Id),
        LanguageData = languageData.ToList(),
        IsReusable = true,
        ContentItemContentFolderGUID = folder.ContentFolderGUID,
    };

    /// <summary>
    /// Gets the parent MagazineIssue ID from a child content item (MagazineArticle or MagazineAuthor).
    /// Uses the ParentId field directly.
    /// </summary>
    /// <param name="childItem">The child content item (MagazineArticle or MagazineAuthor)</param>
    /// <returns>The ID of the parent magazine issue, or Guid.Empty if not found</returns>
    private static Guid GetParentMagazineIssueId(ContentItem childItem)
    {
        // Use ParentId field directly - this is much more reliable than URL parsing
        if (!string.IsNullOrEmpty(childItem.ParentId) && Guid.TryParse(childItem.ParentId, out var parentId))
        {
            return parentId;
        }

        // Return empty GUID if ParentId is not available or invalid
        return Guid.Empty;
    }

    /// <summary>
    /// Extracts a folder path from an item's URL for listing page lookup.
    /// For NewsItem URLs with a date prefix, returns yyyy/mm/dd; otherwise returns empty.
    /// </summary>
    /// <param name="itemDefaultUrl">The item's default URL</param>
    /// <returns>Folder path like "yyyy/mm/dd" or empty string</returns>
    private static string ExtractDateFolderPathFromItemUrl(string? itemDefaultUrl)
    {
        if (string.IsNullOrWhiteSpace(itemDefaultUrl))
        {
            return string.Empty;
        }

        string[] urlSegments = itemDefaultUrl.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (urlSegments.Length >= 3)
        {
            string y = urlSegments[0];
            string m = urlSegments[1];
            string d = urlSegments[2];
            if (int.TryParse(y, out int year) &&
                int.TryParse(m, out int month) &&
                int.TryParse(d, out int day) &&
                year >= 1900 && year <= 2100 &&
                month >= 1 && month <= 12 &&
                day >= 1 && day <= 31)
            {
                return $"{year:D4}/{month:D2}/{day:D2}";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Creates former URLs for NewsItem content using the news listing root path and ItemDefaultUrl.
    /// Example: https://www.elfaonline.org/news-and-publications/industry-news/read/2024/10/17/equipment-finance-industry-maintains-high-confidence-in-october
    /// </summary>
    /// <param name="source">The NewsItem source content</param>
    /// <param name="newsListingRootPath">The root path for news listings (e.g., "/news-and-publications/industry-news")</param>
    /// <param name="dependenciesModel">Content dependencies for language handling</param>
    /// <returns>List of former URLs for the NewsItem</returns>
    private List<PageFormerUrlModel> CreateNewsItemFormerUrls(ContentItem source, string newsListingRootPath, ContentDependencies dependenciesModel)
    {
        var formerUrls = new List<PageFormerUrlModel>();

        if (string.IsNullOrEmpty(source.ItemDefaultUrl))
        {
            logger.LogWarning("NewsItem {ItemId} ({ItemTitle}) has no ItemDefaultUrl. Cannot create former URLs.", source.Id, source.Title);
            return formerUrls;
        }

        var currentSite = contentHelper.GetCurrentSite();
        if (currentSite == null)
        {
            logger.LogWarning("Current site not found. Cannot create former URLs for NewsItem {ItemId}.", source.Id);
            return formerUrls;
        }

        // Create the former URL by combining the news listing root with /read and ItemDefaultUrl
        // Remove leading slash from ItemDefaultUrl if present to avoid double slashes
        string itemUrl = source.ItemDefaultUrl.TrimStart('/');
        string formerUrlPath = $"{newsListingRootPath.TrimEnd('/')}/read/{itemUrl}";

        foreach (var siteCulture in currentSite.SystemCultures)
        {
            var culture = dependenciesModel.ContentLanguages.Values.FirstOrDefault(x => x.ContentLanguageCultureFormat == siteCulture.Culture);

            if (culture == null)
            {
                continue;
            }

            if (ValidationHelper.GetBoolean(culture.ContentLanguageIsDefault, false))
            {
                // Default culture - use the former URL as-is
                formerUrls.Add(new PageFormerUrlModel
                {
                    FormerUrlPath = formerUrlPath.TrimStart('/'),
                    LanguageName = culture.ContentLanguageName
                });

                logger.LogDebug("Created former URL for NewsItem {ItemId} in default culture {Culture}: {FormerUrl}",
                    source.Id, culture.ContentLanguageName, formerUrlPath);
            }
            else
            {
                // Check if there's an alternate language version
                var alternateLanguageContentItem = source.AlternateLanguageContentItems.Find(x => x.Culture == culture.ContentLanguageCultureFormat);

                if (alternateLanguageContentItem != null && !string.IsNullOrEmpty(alternateLanguageContentItem.Url))
                {
                    // Use the alternate language URL
                    string alternateItemUrl = contentHelper.GetRelativeUrl(alternateLanguageContentItem.Url).TrimStart('/');
                    string alternateFormerUrlPath = $"{newsListingRootPath.TrimEnd('/')}/read/{alternateItemUrl}";

                    formerUrls.Add(new PageFormerUrlModel
                    {
                        FormerUrlPath = alternateFormerUrlPath.TrimStart('/'),
                        LanguageName = culture.ContentLanguageName
                    });

                    logger.LogDebug("Created former URL for NewsItem {ItemId} in alternate culture {Culture}: {FormerUrl}",
                        source.Id, culture.ContentLanguageName, alternateFormerUrlPath);
                }
                else
                {
                    // Fallback: prefix the culture name to the default URL
                    formerUrls.Add(new PageFormerUrlModel
                    {
                        FormerUrlPath = $"{culture.ContentLanguageName}{formerUrlPath}".TrimStart('/'),
                        LanguageName = culture.ContentLanguageName
                    });

                    logger.LogDebug("Created fallback former URL for NewsItem {ItemId} in culture {Culture}: {FormerUrl}",
                        source.Id, culture.ContentLanguageName, $"{culture.ContentLanguageName}{formerUrlPath}");
                }
            }
        }

        return formerUrls;
    }

    /// <summary>
    /// Extracts year/month path from NewsItem URL.
    /// Example: "2025/08/13/article-title" → "2025/08"
    /// </summary>
    /// <param name="itemDefaultUrl">The NewsItem ItemDefaultUrl</param>
    /// <returns>Year/month path (e.g., "2025/08") or empty string if not found</returns>
    private static string ExtractYearMonthFromNewsItemUrl(string itemDefaultUrl)
    {
        if (string.IsNullOrEmpty(itemDefaultUrl))
        {
            return string.Empty;
        }

        // Remove leading/trailing slashes and split the URL
        string[] urlSegments = itemDefaultUrl.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Check if we have at least year and month segments (expecting format: year/month/day/article-title)
        if (urlSegments.Length >= 2)
        {
            string yearSegment = urlSegments[0];
            string monthSegment = urlSegments[1];

            // Validate that the first two segments look like year and month
            if (int.TryParse(yearSegment, out int year) &&
                int.TryParse(monthSegment, out int month) &&
                year >= 1900 && year <= 2100 && // Reasonable year range
                month >= 1 && month <= 12) // Valid month range
            {
                return $"{yearSegment}/{monthSegment}";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets the NewsItem URL without the date components (year/month/day).
    /// Example: "2025/08/13/article-title" → "/article-title"
    /// </summary>
    /// <param name="itemDefaultUrl">The NewsItem ItemDefaultUrl</param>
    /// <returns>URL without date components, starting with slash</returns>
    private static string GetNewsItemUrlWithoutDate(string itemDefaultUrl)
    {
        if (string.IsNullOrEmpty(itemDefaultUrl))
        {
            return string.Empty;
        }

        // Remove leading/trailing slashes and split the URL
        string[] urlSegments = itemDefaultUrl.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Check if we have the expected date format (year/month/day/...
        if (urlSegments.Length >= 4)
        {
            string yearSegment = urlSegments[0];
            string monthSegment = urlSegments[1];
            string daySegment = urlSegments[2];

            // Validate that the first three segments look like year/month/day
            if (int.TryParse(yearSegment, out int year) &&
                int.TryParse(monthSegment, out int month) &&
                int.TryParse(daySegment, out int day) &&
                year >= 1900 && year <= 2100 && // Reasonable year range
                month >= 1 && month <= 12 && // Valid month range
                day >= 1 && day <= 31) // Valid day range
            {
                // Return the remaining segments (everything after day) as the article path
                string[] remainingSegments = urlSegments.Skip(3).ToArray();
                return remainingSegments.Length > 0 ? "/" + string.Join("/", remainingSegments) : string.Empty;
            }
        }

        // If the URL doesn't match the expected date format, return the original URL
        return "/" + itemDefaultUrl.TrimStart('/');
    }
}
