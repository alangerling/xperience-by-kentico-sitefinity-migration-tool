using System.Text.Json;

using CMS.ContentEngine;
using CMS.ContentEngine.Internal;
using CMS.Helpers;

using HtmlAgilityPack;

using Kentico.Xperience.UMT.Model;

using Microsoft.Extensions.Logging;

using Migration.Toolkit.Data.Configuration;
using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Core.Factories;
using Migration.Toolkit.Sitefinity.Core.Helpers;
using Migration.Toolkit.Sitefinity.FieldTypes;
using Migration.Toolkit.Sitefinity.Model;

using Newtonsoft.Json.Linq;

using Progress.Sitefinity.RestSdk.Dto;

namespace Migration.Toolkit.Sitefinity.Helpers;

/// <summary>
/// Defines mapping from Sitefinity content type to custom Kentico content type
/// </summary>
public class ContentTypeMapping
{
    /// <summary>
    /// Original Sitefinity type name (e.g., "MagazineArticle")
    /// </summary>
    public required string SitefinityTypeName { get; set; }

    /// <summary>
    /// Target Kentico class name (e.g., "custom.MagazineArticle")
    /// </summary>
    public required string KenticoClassName { get; set; }

    /// <summary>
    /// Field mappings from Sitefinity field names to Kentico field names
    /// Key: Sitefinity field name, Value: Kentico field name
    /// </summary>
    public Dictionary<string, string> FieldMappings { get; set; } = [];
}

internal class ContentHelper(ILogger<ContentHelper> logger,
                                ITypeProvider typeProvider,
                                IFieldTypeFactory fieldTypeFactory,
                                ISiteProvider siteProvider,
                                SitefinityDataConfiguration dataConfiguration) : IContentHelper
{
    private IEnumerable<Site>? sites;

    /// <summary>
    /// Content type mappings for custom Kentico content types
    /// TODO: This should be moved to configuration or injected as a service
    /// </summary>
    private static readonly Dictionary<string, ContentTypeMapping> contentTypeMappings = new()
    {
        {
            "elfaold.Image",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Image",
                KenticoClassName = "ContentBase.Image",
                FieldMappings = new Dictionary<string, string>
                {
                    { "SelectedImage", "ImageAsset" },
                    //{ "UrlSlug", "UrlName" },
                }
            }
        },
        {
            "elfaold.Download",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Download",
                KenticoClassName = "ContentBase.DownloadFile",
                FieldMappings = new Dictionary<string, string>
                {
                    { "SelectedFile", "DownloadAsset" },
                    //{ "UrlSlug", "UrlName" },
                }
            }
        },
        {
            "elfaold.Video",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Video",
                KenticoClassName = "ContentBase.Video",
                FieldMappings = new Dictionary<string, string>
                {
                    { "SelectedVideo", "VideoAsset" },
                    //{ "UrlSlug", "UrlName" },
                }
            }
        },
        {
            "elfaold.PageNode",
            new ContentTypeMapping
            {
                SitefinityTypeName = "PageNode",
                KenticoClassName = "ContentBase.ContentPage",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    //{ "UrlSlug", "UrlName" },
                }
            }
        },
        {
            "elfaold.State",
            new ContentTypeMapping
            {
                SitefinityTypeName = "State",
                KenticoClassName = "Elfa.State",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    //{ "UrlSlug", "UrlName" },
                }
            }
        },
        {
            "elfaold.TaxManualItem",
            new ContentTypeMapping
            {
                SitefinityTypeName = "TaxManualItem",
                KenticoClassName = "Elfa.TaxManualItem",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "PageCopyHtml1", "FullText" },
                    { "TaxManualCategory", "taxmanualcategories" },
                    { "PublicationDate", "ReleaseDate" },
                    { "RelatedFilesDownloads", "Documents" },
                    { "PageImage", "Image" },
                    //{ "UrlSlug", "UrlName" },
                    { "StateTaxonomy", "" }
                }
            }
        },
        {
            "elfaold.CompendiumIssue",
            new ContentTypeMapping
            {
                SitefinityTypeName = "CompendiumIssue",
                KenticoClassName = "Elfa.CompendiumIssue",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "PageCopyHtml1", "Description" },
                    { "PageCopyHtml2", "Comments" },
                    { "Question", "Question" },
                    { "RelatedState", "" },
                    { "PublicationAuthor", "LastReviewAuthor" },
                    { "PublicationDate", "LastReviewDate" },
                    //{ "UrlSlug", "UrlName" },
                    { "PublicationAuthorPages", "Authors" }
                }
            }
        },
        {
            "elfaold.CompendiumAuthor",
            new ContentTypeMapping
            {
                SitefinityTypeName = "CompendiumAuthor",
                KenticoClassName = "Elfa.CompendiumAuthor",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "ContactPhoneOffice", "Phone" },
                    { "ContactEmail", "Email" },
                    { "PersonOrganization", "LawFirmName" },
                    { "PersonWebsiteUrl", "LawFirmWebsite" },
                    //{ "UrlSlug", "UrlName" },
                }
            }
        },
        {
            "elfaold.MagazineIssue",
            new ContentTypeMapping
            {
                SitefinityTypeName = "MagazineIssue",
                KenticoClassName = "Elfa.MagazineIssue",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "MagazineIssueMonth", "IssueMonth" },
                    { "MagazineIssueYear", "IssueYear" },
                    { "PageImage", "CoverImage" },
                    { "MagazineIssueSponsors", "Sponsors" },
                    { "PublicationDate", "PublicationDate" },
                    //{ "UrlSlug", "UrlName" }
                }
            }
        },
        {
            "elfaold.MagazineSponsor",
            new ContentTypeMapping
            {
                SitefinityTypeName = "MagazineSponsor",
                KenticoClassName = "Elfa.Organization",
                FieldMappings = new Dictionary<string, string>
                {
                    { "OrganizationName", "Title" },
                    { "OrganizationUrl", "URL" },
                }
            }
        },
        {
            "elfaold.MagazineAuthor",
            new ContentTypeMapping
            {
                SitefinityTypeName = "MagazineAuthor",
                KenticoClassName = "ContentBase.PersonDetail",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "ListingItemThumbnail", "Photo" },
                }
            }
        },
        {
            "elfaold.MagazineArticle",
            new ContentTypeMapping
            {
                SitefinityTypeName = "MagazineArticle",
                KenticoClassName = "ContentBase.ArticleDetail",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "PageDescription", "Summary" },
                    { "PageCopyHtml1", "Photo" },
                    { "ListingItemThumbnail", "Photo" },
                    { "ArticleAuthor", "AuthorByline" },
                    { "AuthorPages", "ArticleAuthor" },
                    { "PageImage", "HeroImage" },
                    //{ "URLSlug", "UrlName" },
                }
            }
        }
    };

    /// <summary>
    /// Gets the mapped Kentico class name for a given Sitefinity type name, or returns the original if no mapping exists
    /// </summary>
    /// <param name="sitefinityTypeName">The Sitefinity type name</param>
    /// <param name="originalClassName">The original class name from DataClassModel</param>
    /// <returns>The mapped Kentico class name or the original if no mapping exists</returns>
    public string GetMappedClassName(string? sitefinityTypeName, string? originalClassName)
    {
        if (string.IsNullOrEmpty(sitefinityTypeName) || !contentTypeMappings.TryGetValue(sitefinityTypeName, out var mapping))
        {
            return originalClassName ?? string.Empty;
        }

        logger.LogInformation("Mapping Sitefinity type '{SitefinityType}' to Kentico class '{KenticoClass}'",
            sitefinityTypeName, mapping.KenticoClassName);

        return mapping.KenticoClassName;
    }

    public IEnumerable<ContentItemLanguageData> GetLanguageData(ContentDependencies contentDependencies, ICultureSdkItem cultureSdkItem, DataClassModel dataClassModel, UserInfoModel? createdByUser)
    {
        var languageData = new List<ContentItemLanguageData>();

        foreach (var culture in contentDependencies.ContentLanguages.Values)
        {
            if (culture.ContentLanguageIsDefault == null)
            {
                continue;
            }

            if (culture.ContentLanguageName == null)
            {
                continue;
            }

            if (ValidationHelper.GetBoolean(culture.ContentLanguageIsDefault, false))
            {
                var contentLanguageData = GetLanguageDataInternal(contentDependencies, culture.ContentLanguageName, cultureSdkItem, dataClassModel, createdByUser);

                if (contentLanguageData == null)
                {
                    logger.LogWarning("Failed to parse language data for default culture: {Culture}. Skipping content item {ItemDefaultUrl}.", culture.ContentLanguageName, cultureSdkItem.UrlName);
                    continue;
                }

                languageData.Add(contentLanguageData);
            }
            else
            {
                foreach (var alternateLanguageContentItem in cultureSdkItem.AlternateLanguageContentItems)
                {
                    if (alternateLanguageContentItem.Culture == null || string.IsNullOrEmpty(alternateLanguageContentItem.UrlName) || alternateLanguageContentItem.UrlName.Equals(cultureSdkItem.UrlName))
                    {
                        continue;
                    }

                    if (alternateLanguageContentItem.Culture.Equals(culture.ContentLanguageCultureFormat))
                    {
                        var contentLanguageData = GetLanguageDataInternal(contentDependencies, culture.ContentLanguageName, alternateLanguageContentItem, dataClassModel, createdByUser);

                        if (contentLanguageData == null)
                        {
                            logger.LogWarning("Failed to parse language data for alternate culture: {Culture}. Skipping content item {ItemDefaultUrl}.", culture.ContentLanguageName, alternateLanguageContentItem.UrlName);
                            continue;
                        }

                        languageData.Add(contentLanguageData);
                    }
                }
            }
        }

        return languageData;
    }

    private ContentItemLanguageData? GetLanguageDataInternal(ContentDependencies contentDependencies, string languageName, ICultureSdkItem cultureSdkItem, DataClassModel dataClassModel, UserInfoModel? user)
    {
        if (string.IsNullOrEmpty(cultureSdkItem.UrlName))
        {
            return default;
        }

        var types = typeProvider.GetAllTypes();

        var type = types.FirstOrDefault(x => x.Id == dataClassModel.ClassGUID);

        if (type == null || type.Fields == null)
        {
            return default;
        }

        var contentItemData = new Dictionary<string, object?>();
        var newContentItemData = new Dictionary<string, object?>();

        foreach (var field in type.Fields)
        {
            var fieldType = fieldTypeFactory.CreateFieldType(field.WidgetTypeName);

            if (field.Name == null)
            {
                continue;
            }

            if (Constants.ExcludedFields.Contains(field.Name))
            {
                continue;
            }

            try
            {
                if (cultureSdkItem is SdkItem sdkItem)
                {
                    object? data = fieldType.GetData(sdkItem, field.Name);

                    // If data is null or empty string, add directly and skip further processing
                    if (data == null || (data is string str && string.IsNullOrEmpty(str)))
                    {
                        contentItemData.Add(field.Name, data);
                        continue;
                    }

                    if (fieldType is HtmlFieldType)
                    {
                        data = UpdateUrlsToPermanent(contentDependencies, ValidationHelper.GetString(data, ""));
                    }

                    if (fieldType is LinkFieldType)
                    {
                        var links = JsonSerializer.Deserialize<IEnumerable<Link>>(ValidationHelper.GetString(data, ""), new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (links != null)
                        {
                            foreach (var link in links)
                            {
                                if (link.Href == null)
                                {
                                    continue;
                                }

                                link.Href = GetPermalink(contentDependencies, link.Href);
                            }
                        }

                        data = JsonSerializer.Serialize(links);
                    }

                    // Handle Newtonsoft.Json JToken objects (JArray, JObject, etc.)
                    if (data is JToken jToken)
                    {
                        data = jToken.ToString(); // Serialize JToken to JSON string
                    }

                    contentItemData.Add(field.Name, data);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cannot get data for {FieldName} field.", field.Name);
            }
        }

        newContentItemData = contentItemData;

        // Apply content type mappings if they exist
        string? sitefinityTypeName = dataClassModel.ClassName;
        if (!string.IsNullOrEmpty(sitefinityTypeName) && contentTypeMappings.TryGetValue(sitefinityTypeName, out var mapping))
        {
            newContentItemData = []; // Reset for mapped content types

            // Apply field mappings
            foreach (var fieldMapping in mapping.FieldMappings)
            {
                string sitefinityFieldName = fieldMapping.Value;
                string kenticoFieldName = fieldMapping.Key;

                // Skip empty Kentico field names (used for fields that should be excluded)
                if (string.IsNullOrEmpty(kenticoFieldName))
                {
                    continue;
                }

                // Map the field if it exists in the original data
                if (contentItemData.TryGetValue(sitefinityFieldName, out object? fieldValue))
                {
                    newContentItemData[kenticoFieldName] = fieldValue;
                }
            }

            logger.LogDebug("Applied field mappings for content type '{ContentType}'. Mapped {MappedFields} fields.",
                sitefinityTypeName, newContentItemData.Count);
        }

        return new ContentItemLanguageData
        {
            DisplayName = cultureSdkItem.Title.Length > 100 ? cultureSdkItem.Title[..100] : cultureSdkItem.Title,
            LanguageName = languageName,
            UserGuid = user?.UserGUID,
            ContentItemData = newContentItemData,
            VersionStatus = VersionStatus.Published,
        };
    }

    public string GetName(string title, Guid id, int length = 100)
    {
        // Use the full GUID and trim the title to fit within the length constraint
        string guidString = id.ToString();
        int maxTitleLength = Math.Max(0, length - guidString.Length - 1); // 1 for the hyphen
        string safeTitle = ValidationHelper.GetCodeName(title);
        if (safeTitle.Length > maxTitleLength)
        {
            safeTitle = safeTitle[..maxTitleLength];
        }
        string name = $"{safeTitle}-{guidString}".Replace(".", "-");
        return name;
    }

    public Site? GetCurrentSite()
    {
        sites ??= siteProvider.GetSites();
        return sites.FirstOrDefault(x => (x.LiveUrl != null && x.LiveUrl.Equals(dataConfiguration.SitefinitySiteDomain)) || (x.StagingUrl != null && x.StagingUrl.Equals(dataConfiguration.SitefinitySiteDomain)));
    }

    public ChannelModel? GetCurrentChannel(IEnumerable<ChannelModel> channels)
    {
        var currentSite = GetCurrentSite();
        return channels.FirstOrDefault(x => x.ChannelGUID.Equals(currentSite?.Id));
    }

    public List<PageUrlModel> GetPageUrls(ContentDependencies dependenciesModel, ICultureSdkItem source, string? rootPath = null, string? pagePath = null)
    {
        var pageUrls = new List<PageUrlModel>();

        string pageUrl = GetUrl(source, rootPath, pagePath);

        var currentSite = GetCurrentSite();

        if (currentSite == null)
        {
            logger.LogWarning("Current site not found. Cannot get page urls for {UrlName}.", source.UrlName);
            return pageUrls;
        }

        foreach (var siteCulture in currentSite.SystemCultures)
        {
            var culture = dependenciesModel.ContentLanguages.Values.FirstOrDefault(x => x.ContentLanguageCultureFormat == siteCulture.Culture);

            if (culture == null || string.IsNullOrEmpty(source.Url))
            {
                continue;
            }

            if (ValidationHelper.GetBoolean(culture.ContentLanguageIsDefault, false))
            {
                pageUrls.Add(new PageUrlModel
                {
                    UrlPath = pageUrl.TrimStart('/'),
                    LanguageName = culture.ContentLanguageName,
                    PathIsDraft = false
                });
            }
            else
            {
                var alternateLanguageContentItem = source.AlternateLanguageContentItems.Find(x => x.Culture == culture.ContentLanguageCultureFormat);

                if (alternateLanguageContentItem == null || string.IsNullOrEmpty(alternateLanguageContentItem.Url))
                {
                    pageUrls.Add(new PageUrlModel
                    {
                        UrlPath = culture.ContentLanguageName + pageUrl,
                        LanguageName = culture.ContentLanguageName,
                        PathIsDraft = false
                    });

                    continue;
                }

                pageUrls.Add(new PageUrlModel
                {
                    UrlPath = GetUrl(alternateLanguageContentItem, rootPath, pagePath).TrimStart('/'),
                    LanguageName = culture.ContentLanguageName,
                    PathIsDraft = false
                });
            }
        }

        return pageUrls;
    }

    private string GetUrl(ICultureSdkItem source, string? rootPath, string? pagePath)
    {
        string pageUrl = GetRelativeUrl(source.Url);

        if (!string.IsNullOrEmpty(pagePath))
        {
            pageUrl = pagePath;
        }

        if (!string.IsNullOrEmpty(rootPath))
        {
            pageUrl = rootPath + pageUrl;
        }

        return pageUrl;
    }

    public string GetParentPath(string? path) => TreePathUtils.RemoveLastPathSegment(path);

    public string RemovePathSegmentsFromStart(string path, int numberOfSegments)
    {
        string[] segments = path.Split('/');
        int remainingSegments = segments.Length - numberOfSegments;
        if (remainingSegments <= 0)
        {
            return "";
        }
        string newPath = $"/{string.Join("/", segments.TakeLast(remainingSegments))}";
        return newPath;
    }

    public string GetRelativeUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        if (url.StartsWith('/'))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.PathAndQuery;
        }

        return url;
    }

    public string UpdateUrlsToPermanent(IMediaDependencies mediaDependencies, string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var imgNodes = document.DocumentNode.SelectNodes("//img");
        if (imgNodes != null)
        {
            UpdateUrls(mediaDependencies, imgNodes, "src");
        }
        var aNodes = document.DocumentNode.SelectNodes("//a");
        if (aNodes != null)
        {
            UpdateUrls(mediaDependencies, aNodes, "href");
        }

        return document.DocumentNode.OuterHtml;
    }

    private void UpdateUrls(IMediaDependencies mediaDependencies, IEnumerable<HtmlNode> htmlNodes, string attributeName)
    {
        if (htmlNodes == null)
        {
            return;
        }

        foreach (var htmlNode in htmlNodes)
        {
            string? attributeValue = htmlNode.GetAttributeValue(attributeName, string.Empty);

            if (string.IsNullOrWhiteSpace(attributeValue))
            {
                continue;
            }

            string? permaLinkUrl = GetPermalink(mediaDependencies, attributeValue);

            if (permaLinkUrl == null)
            {
                continue;
            }

            htmlNode.SetAttributeValue(attributeName, permaLinkUrl);
        }
    }

    private string? GetPermalink(IMediaDependencies mediaDependencies, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        url = url.TrimStart('~');

        if (url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // Only process relative URLs or URLs from the configured domain
        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            string? configuredDomain = dataConfiguration.SitefinitySiteDomain?.TrimEnd('/');

            // Extract only the host (e.g., "www.leasefoundation.org")
            string urlHost = absoluteUri.Host;

            // Check if it does NOT match the configured domain
            if (!urlHost.Equals(configuredDomain, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("URL {Url} does not belong to configured domain {Domain}. Skipping processing.", url, configuredDomain);
                return url;
            }
        }
        else if (!Uri.TryCreate(url, UriKind.Relative, out _))
        {
            logger.LogWarning("Invalid URL format: {Url}. Skipping processing.", url);
            return url;
        }

        ContentItemSimplifiedModel? mediaFile = null;
        string urlToSearch = string.Empty;

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUriForSearch))
        {
            urlToSearch = URLHelper.RemoveQuery(absoluteUriForSearch.PathAndQuery);
        }
        else if (Uri.TryCreate(url, UriKind.Relative, out _))
        {
            urlToSearch = URLHelper.RemoveQuery(url);
        }

        mediaFile = FindMediaFileByUrl(mediaDependencies, urlToSearch);

        if (mediaFile is null)
        {
            logger.LogInformation("Could not find media file for {PathAndQuery}", url);
            return url;
        }

        // Extract filename from the original URL
        string fileName = Path.GetFileName(URLHelper.RemoveQuery(url));
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = mediaFile.Name ?? "file";
        }

        // Get field definition GUIDs dynamically from TypeProvider instead of hardcoding
        var languageData = mediaFile.LanguageData?.FirstOrDefault();
        string? assetFieldGuid = null;

        if (languageData?.ContentItemData is not null)
        {
            // Get the asset field GUID from TypeProvider based on which field exists in the content item
            assetFieldGuid = GetAssetFieldGuidFromTypeProvider(languageData.ContentItemData);
        }

        if (string.IsNullOrEmpty(assetFieldGuid))
        {
            logger.LogWarning("Could not determine asset field type for media file {ContentItemGUID}. Using content item GUID as fallback.",
                mediaFile.ContentItemGUID);
            assetFieldGuid = mediaFile.ContentItemGUID?.ToString() ?? Guid.Empty.ToString();
        }

        string permalinkUrl = $"/getContentAsset/{mediaFile.ContentItemGUID}/{assetFieldGuid}/{fileName}";

        // Add language parameter if available
        if (!string.IsNullOrEmpty(languageData?.LanguageName))
        {
            permalinkUrl += $"?language={languageData.LanguageName}";
        }

        logger.LogDebug("Generated permalink URL: {PermalinkUrl} for original URL: {OriginalUrl}", permalinkUrl, url);
        return permalinkUrl;
    }

    /// <summary>
    /// Gets the asset field GUID from TypeProvider based on which asset field exists in the content item data.
    /// </summary>
    /// <param name="contentItemData">The content item data to check for asset fields.</param>
    /// <returns>The field definition GUID from TypeProvider, or null if not found.</returns>
    private string? GetAssetFieldGuidFromTypeProvider(Dictionary<string, object?> contentItemData)
    {
        // Define the asset field names and their corresponding hardcoded GUIDs
        string selectedImageFieldGuid = "e477a59e-1df6-4e2f-9986-20ab37342540";
        string selectedVideoFieldGuid = "aaaaaaaa-1df6-4e2f-9986-20ab37342540";
        string selectedFileFieldGuid = "d83a1aaf-18c3-4508-be2c-6950cbee5b16";

        // Check for asset fields and return the corresponding GUID
        if (contentItemData.ContainsKey("SelectedImage"))
        {
            logger.LogDebug("Found asset field SelectedImage, returning hardcoded GUID {FieldGuid}", selectedImageFieldGuid);
            return selectedImageFieldGuid;
        }

        if (contentItemData.ContainsKey("SelectedVideo"))
        {
            logger.LogDebug("Found asset field SelectedVideo, returning hardcoded GUID {FieldGuid}", selectedVideoFieldGuid);
            return selectedVideoFieldGuid;
        }

        if (contentItemData.ContainsKey("SelectedFile"))
        {
            logger.LogDebug("Found asset field SelectedFile, returning hardcoded GUID {FieldGuid}", selectedFileFieldGuid);
            return selectedFileFieldGuid;
        }

        return null;
    }

    private static ContentItemSimplifiedModel? FindMediaFileByUrl(IMediaDependencies mediaDependencies, string targetUrl) => mediaDependencies.MediaFiles.Values.FirstOrDefault(contentItem =>
        // Check all language variants for a matching asset URL
        (contentItem.LanguageData ?? [])
            .Select(languageData => languageData.ContentItemData)
            .Where(contentItemData => contentItemData != null)
            .Any(contentItemData =>
            {
                // Check for various asset URL field names based on content type
                string[] assetUrlFields = new[] { "ImageAssetLegacyUrl", "DownloadAssetLegacyUrl", "VideoAssetLegacyUrl" };

                foreach (string fieldName in assetUrlFields)
                {
                    if (contentItemData!.TryGetValue(fieldName, out object? assetUrlValue) &&
                        assetUrlValue is string assetUrl &&
                        !string.IsNullOrEmpty(assetUrl) &&
                        assetUrl.Equals(targetUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }));
}
