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
                                                 ContentFolderManager contentFolderManager) : UmtAdapterBaseWithDependencies<ContentItem, ContentDependencies, ContentItemSimplifiedModel>(logger)
{
    private readonly Dictionary<Guid, ContentItemSimplifiedModel> detailContentItems = [];

    protected override ContentItemSimplifiedModel? AdaptInternal(ContentItem source, ContentDependencies dependenciesModel)
    {
        var rootFolder = ContentFolderInfo.Provider.GetRootAsync(configuration.KenticoDefaultWorkspaceName).GetAwaiter().GetResult();

        if (rootFolder == null)
        {
            logger.LogWarning("Content Hub root folder not found.");
            return default;
        }

        if (!dependenciesModel.DataClasses.TryGetValue(source.DataClassGuid, out var dataClassModel))
        {
            logger.LogWarning("Data class with ClassGuid of {DataClassGuid} not found. Skipping content item {ItemDefaultUrl}.", source.DataClassGuid, source.ItemDefaultUrl);
            return default;
        }

        var users = dependenciesModel.Users;
        users.TryGetValue(ValidationHelper.GetGuid(source.Owner, Guid.Empty), out var createdByUser);
        var languageData = contentHelper.GetLanguageData(dependenciesModel, source, dataClassModel, createdByUser);

        if (dataClassModel.ClassContentTypeType == null)
        {
            return AdaptReusable(source, dataClassModel, languageData, rootFolder);
        }

        if (dataClassModel.ClassContentTypeType.Equals("Reusable") || dataClassModel.ClassName == "elfa.Program")
        {
            return AdaptReusable(source, dataClassModel, languageData, new ContentFolderInfo { ContentFolderGUID = dataClassModel.ClassGUID ?? rootFolder.ContentFolderGUID });
        }

        if (dataClassModel.ClassContentTypeType.Equals("Website"))
        {
            return AdaptPage(source, dataClassModel, languageData, dependenciesModel);
        }

        return AdaptReusable(source, dataClassModel, languageData, rootFolder);
    }

    private readonly Dictionary<int, Guid> stateContentItemGuids = [];

    private ContentItemSimplifiedModel? AdaptPage(ContentItem source, DataClassModel dataClassModel, IEnumerable<ContentItemLanguageData> languageData, ContentDependencies dependenciesModel)
    {
        var channel = contentHelper.GetCurrentChannel(dependenciesModel.Channels.Values);

        if (channel == null)
        {
            logger.LogWarning("Channel not found for domain: {Domain}. Skipping content item {ItemDefaultUrl}.", dataConfiguration.SitefinitySiteDomain, source.UrlName);
            return default;
        }

        // Use exact match for TypeName instead of Contains
        var pageConfigs = configuration.PageContentTypes?.Where(x => dataClassModel.ClassName != null && dataClassModel.ClassName.EndsWith("." + x.TypeName));

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
                ContentTypeName = dataClassModel.ClassName,
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
            if (dataClassModel.ClassName == "elfa.State")
            {
                string[] segments = (source.ItemDefaultUrl ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
                string stateName = segments.Length > 0 ? segments[0] : string.Empty;
                stateContentItemGuids[stateName.GetHashCode()] = source.Id;
            }

            if (dataClassModel.ClassName is "elfa.TaxManualItem" or "elfa.CompendiumIssue")
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
                    ContentTypeName = dataClassModel.ClassName,
                    Name = contentHelper.GetName(source.Title, source.Id),
                    LanguageData = languageData.ToList(),
                    IsReusable = false,
                    PageData = stateContentItemPageData,
                    ChannelName = channel.ChannelName
                };

                return stateContentItem;
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
                    ContentTypeName = dataClassModel.ClassName,
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

                detailPage.ContentTypeName = dataClassModel.ClassName;
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
            ContentTypeName = dataClassModel.ClassName,
            Name = contentHelper.GetName(source.Title, source.Id),
            LanguageData = languageData.ToList(),
            IsReusable = false,
            PageData = pageData,
            ChannelName = channel.ChannelName
        };

        return pageContentItem;
    }

    private ContentItemSimplifiedModel AdaptReusable(ContentItem source, DataClassModel dataClassModel, IEnumerable<ContentItemLanguageData> languageData, ContentFolderInfo folder) => new()
    {
        ContentItemGUID = source.Id,
        ContentTypeName = dataClassModel.ClassName,
        Name = contentHelper.GetName(source.Title, source.Id),
        LanguageData = languageData.ToList(),
        IsReusable = true,
        ContentItemContentFolderGUID = folder.ContentFolderGUID,
    };
}
