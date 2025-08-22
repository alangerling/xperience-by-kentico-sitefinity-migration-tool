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
using System.Text.Json;

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

    // Holds combined taxonomy tags (IssueMonth + IssueYear) for MagazineIssue items as JSON array of { Identifier }
    private readonly Dictionary<Guid, string> magazineIssueAdditionalCategoryTags = [];

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

                // Capture IssueMonth (Category) and IssueYear (StandardCategory) as combined tags to be applied to child MagazineArticles
                try
                {
                    var defaultLang = languageData.FirstOrDefault();
                    string? issueMonthJson = null;
                    string? issueYearJson = null;

                    if (defaultLang?.ContentItemData != null)
                    {
                        if (defaultLang.ContentItemData.TryGetValue("Category", out object? catVal) && catVal is string catStr && !string.IsNullOrWhiteSpace(catStr) && !catStr.Equals("[]", StringComparison.Ordinal))
                        {
                            issueMonthJson = FilterTaxonomyItems(catStr);
                        }
                        if (defaultLang.ContentItemData.TryGetValue("StandardCategory", out object? priCatVal) && priCatVal is string priCatStr && !string.IsNullOrWhiteSpace(priCatStr) && !priCatStr.Equals("[]", StringComparison.Ordinal))
                        {
                            issueYearJson = FilterTaxonomyItems(priCatStr);
                        }
                    }

                    string combined = "[]";
                    if (!string.IsNullOrWhiteSpace(issueMonthJson) && !string.IsNullOrWhiteSpace(issueYearJson))
                    {
                        combined = MergeTaxonomyArrays(issueMonthJson!, issueYearJson!);
                    }
                    else if (!string.IsNullOrWhiteSpace(issueMonthJson))
                    {
                        combined = issueMonthJson!;
                    }
                    else if (!string.IsNullOrWhiteSpace(issueYearJson))
                    {
                        combined = issueYearJson!;
                    }

                    magazineIssueAdditionalCategoryTags[source.Id] = combined;
                    logger.LogDebug("Stored MagazineIssue tags for Issue {IssueId}: {Tags}", source.Id, combined);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to capture IssueMonth/IssueYear tags for MagazineIssue {IssueId}.", source.Id);
                }

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

                // If this is a MagazineArticle, enrich AdditionalCategories with parent's IssueMonth/IssueYear tags
                if (string.Equals(source.TypeName, "MagazineArticle", StringComparison.OrdinalIgnoreCase))
                {
                    if (parentMagazineIssueGuid != Guid.Empty && magazineIssueAdditionalCategoryTags.TryGetValue(parentMagazineIssueGuid, out string? issueTagsJson))
                    {
                        try
                        {
                            foreach (var lang in languageData)
                            {
                                if (lang.ContentItemData == null)
                                {
                                    continue;
                                }

                                lang.ContentItemData.TryGetValue("AdditionalCategories", out object? existingValueObj);
                                string? existingJson = existingValueObj as string;

                                string resultJson;
                                if (!string.IsNullOrWhiteSpace(existingJson) && !existingJson.Equals("[]", StringComparison.Ordinal) && IsValidJson(existingJson))
                                {
                                    resultJson = MergeTaxonomyArrays(existingJson, issueTagsJson);
                                }
                                else
                                {
                                    resultJson = issueTagsJson;
                                }

                                lang.ContentItemData["AdditionalCategories"] = resultJson;
                            }

                            logger.LogInformation("Applied parent Issue tags to MagazineArticle {ArticleId} ({ArticleTitle}).", source.Id, source.Title);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to merge Issue tags into MagazineArticle AdditionalCategories for {ArticleId}.", source.Id);
                        }
                    }
                    else
                    {
                        logger.LogDebug("No stored Issue tags found for parent {ParentId} when processing MagazineArticle {ArticleId}.", parentMagazineIssueGuid, source.Id);
                    }
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

                // Add Former URLs for MagazineArticle by replacing '/issue/article/' with '/issue/'
                if (string.Equals(source.TypeName, "MagazineArticle", StringComparison.OrdinalIgnoreCase))
                {
                    var formerUrls = CreateMagazineFormerUrls(source, pageConfig.PageRootPath, dependenciesModel);
                    if (formerUrls.Count > 0)
                    {
                        magazineChildPageData.PageFormerUrls = formerUrls;
                        logger.LogInformation("MagazineArticle {ItemId} ({ItemTitle}) created with {FormerUrlCount} former URLs.",
                            source.Id, source.Title, formerUrls.Count);
                    }
                }

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
                string candidateFolderPath = string.Empty;

                // Special handling for Event items to extract folder path from URL
                if (source.TypeName == "Event")
                {
                    candidateFolderPath = ExtractEventFolderPathFromItemUrl(source.ItemDefaultUrl);
                }
                else
                {
                    // For NewsItems, try to extract date-based folder path
                    candidateFolderPath = ExtractDateFolderPathFromItemUrl(source.ItemDefaultUrl);
                }

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
                // Add Former URLs for MagazineIssue by replacing '/issue/article/' with '/issue/'
                else if (string.Equals(source.TypeName, "MagazineIssue", StringComparison.OrdinalIgnoreCase))
                {
                    var formerUrls = CreateMagazineFormerUrls(source, pageConfig.PageRootPath, dependenciesModel);
                    if (formerUrls.Count > 0)
                    {
                        listingChildPageData.PageFormerUrls = formerUrls;
                        logger.LogInformation("MagazineIssue {ItemId} ({ItemTitle}) created with {FormerUrlCount} former URLs.",
                            source.Id, source.Title, formerUrls.Count);
                    }
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
    /// Extracts subfolder path from an event item's URL for organizing events under parent listing pages.
    /// Removes the last URL segment (the event name) and returns the remaining path as the subfolder structure.
    /// Example: "events/2025/conferences/tech-summit" returns "events/2025/conferences"
    /// </summary>
    /// <param name="itemDefaultUrl">The event item's default URL</param>
    /// <returns>Subfolder path for the event or empty string if none</returns>
    private static string ExtractEventFolderPathFromItemUrl(string? itemDefaultUrl)
    {
        if (string.IsNullOrWhiteSpace(itemDefaultUrl))
        {
            return string.Empty;
        }

        // Normalize via ContentHelper-like logic: strip default-calendar
        string relative = itemDefaultUrl.Trim();
        if (Uri.TryCreate(relative, UriKind.Absolute, out var abs))
        {
            relative = abs.PathAndQuery;
        }

        relative = relative.Trim('/');
        if (string.IsNullOrEmpty(relative))
        {
            return string.Empty;
        }

        // Remove the specific segment 'default-calendar'
        string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] filtered = parts.Where(p => !p.Equals("default-calendar", StringComparison.OrdinalIgnoreCase)).ToArray();
        string cleaned = string.Join('/', filtered);

        int lastSlash = cleaned.LastIndexOf('/');
        return lastSlash > 0 ? cleaned[..lastSlash] : string.Empty;
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
    /// Creates former URLs for MagazineIssue and MagazineArticle by replacing '/issue/article/' with '/issue/'.
    /// Uses the PageRootPath and ItemDefaultUrl to compose the current path, then applies the replacement.
    /// Handles default and alternate cultures similarly to news items.
    /// </summary>
    /// <param name="source">The MagazineIssue or MagazineArticle content</param>
    /// <param name="magazineListingRootPath">The magazine listing root path (e.g., "/news-and-publications/magazine/issue/article")</param>
    /// <param name="dependenciesModel">Content dependencies for language handling</param>
    /// <returns>List of former URLs for the item</returns>
    private List<PageFormerUrlModel> CreateMagazineFormerUrls(ContentItem source, string magazineListingRootPath, ContentDependencies dependenciesModel)
    {
        var formerUrls = new List<PageFormerUrlModel>();

        if (string.IsNullOrWhiteSpace(source.ItemDefaultUrl))
        {
            logger.LogWarning("{ContentType} {ItemId} ({ItemTitle}) has no ItemDefaultUrl. Cannot create former URLs.",
                source.TypeName, source.Id, source.Title);
            return formerUrls;
        }

        var currentSite = contentHelper.GetCurrentSite();
        if (currentSite == null)
        {
            logger.LogWarning("Current site not found. Cannot create former URLs for {ContentType} {ItemId}.",
                source.TypeName, source.Id);
            return formerUrls;
        }

        string itemUrl = source.ItemDefaultUrl.TrimStart('/');
        string currentFullPath = $"{magazineListingRootPath.TrimEnd('/')}/{itemUrl}";
        string formerPath = ReplaceIssueArticleSegment(currentFullPath).TrimStart('/');

        foreach (var siteCulture in currentSite.SystemCultures)
        {
            var culture = dependenciesModel.ContentLanguages.Values.FirstOrDefault(x => x.ContentLanguageCultureFormat == siteCulture.Culture);
            if (culture == null)
            {
                continue;
            }

            if (ValidationHelper.GetBoolean(culture.ContentLanguageIsDefault, false))
            {
                formerUrls.Add(new PageFormerUrlModel
                {
                    FormerUrlPath = formerPath,
                    LanguageName = culture.ContentLanguageName
                });
            }
            else
            {
                var alternateLanguageContentItem = source.AlternateLanguageContentItems.Find(x => x.Culture == culture.ContentLanguageCultureFormat);
                if (alternateLanguageContentItem != null && !string.IsNullOrEmpty(alternateLanguageContentItem.Url))
                {
                    string altItemUrl = contentHelper.GetRelativeUrl(alternateLanguageContentItem.Url).TrimStart('/');
                    string altCurrentFullPath = $"{magazineListingRootPath.TrimEnd('/')}/{altItemUrl}";
                    string altFormerPath = ReplaceIssueArticleSegment(altCurrentFullPath).TrimStart('/');

                    formerUrls.Add(new PageFormerUrlModel
                    {
                        FormerUrlPath = altFormerPath,
                        LanguageName = culture.ContentLanguageName
                    });
                }
                else
                {
                    // Fallback: prefix the culture name before the path
                    formerUrls.Add(new PageFormerUrlModel
                    {
                        FormerUrlPath = $"{culture.ContentLanguageName}{(formerPath.StartsWith('/') ? string.Empty : "/")}{formerPath}".TrimStart('/'),
                        LanguageName = culture.ContentLanguageName
                    });
                }
            }
        }

        return formerUrls;
    }

    private static string ReplaceIssueArticleSegment(string input)
    {
        return input.Replace("/issue/article/", "/issue/", StringComparison.OrdinalIgnoreCase);
    }

    // Helpers to merge taxonomy arrays for tags
    private static string MergeTaxonomyArrays(string array1, string array2)
    {
        try
        {
            if (!IsValidJson(array1) || !IsValidJson(array2))
            {
                if (IsValidJson(array1))
                {
                    return FilterTaxonomyItems(array1);
                }
                if (IsValidJson(array2))
                {
                    return FilterTaxonomyItems(array2);
                }
                return "[]";
            }

            var items1 = JsonSerializer.Deserialize<List<ContentRelatedItem>>(array1) ?? [];
            var items2 = JsonSerializer.Deserialize<List<ContentRelatedItem>>(array2) ?? [];

            var mergedItems = items1
                .Concat(items2)
                .Where(item => item.Identifier != Guid.Empty)
                .GroupBy(item => item.Identifier)
                .Select(group => group.First())
                .ToList();

            var taxonomyItems = mergedItems.Select(item => new { item.Identifier }).ToList();

            return JsonSerializer.Serialize(taxonomyItems);
        }
        catch
        {
            return IsValidJson(array1) ? FilterTaxonomyItems(array1) : "[]";
        }
    }

    private static string FilterTaxonomyItems(string jsonArray)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<ContentRelatedItem>>(jsonArray) ?? [];
            var taxonomyItems = items
                .Where(item => item.Identifier != Guid.Empty)
                .Select(item => new { item.Identifier })
                .ToList();
            return JsonSerializer.Serialize(taxonomyItems);
        }
        catch
        {
            return IsValidJson(jsonArray) ? jsonArray : "[]";
        }
    }

    private static bool IsValidJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
