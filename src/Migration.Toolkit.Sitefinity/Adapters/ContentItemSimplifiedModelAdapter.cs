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
            logger.LogDebug("Content type is Reusable for {ItemId} ({ItemTitle). Using AdaptReusable.", source.Id, source.Title);
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
                string stateName = segments.Length > 0 ? segments[0] : string.Empty;
                stateContentItemGuids[stateName.GetHashCode()] = source.Id;
            }

            if (source.TypeName is "CompendiumIssue")
            {
                var authors = source.GetValue<IEnumerable<ContentItem>>("Authors");

                // Extract the state name from the CompendiumIssue's URL to map authors to state folders
                string[] segments = (source.ItemDefaultUrl ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
                string stateName = segments.Length > 0 ? segments[0] : string.Empty;
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
                string stateName = segments.Length > 0 ? segments[0] : string.Empty;
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
                    string stateName = pathSegments.Length > 1 ? pathSegments[^1] : string.Empty; // Get last segment (state name)

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
                var listingPage = dependenciesModel.WebPages?.Values
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
}
