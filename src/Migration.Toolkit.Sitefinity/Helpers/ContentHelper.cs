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
    /// </summary>
    private static readonly Dictionary<string, ContentTypeMapping> contentTypeMappings = new()
    {
        {
            "Image",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Image",
                KenticoClassName = "ContentBase.Image",
                FieldMappings = new Dictionary<string, string>
                {
                    { "SelectedImage", "ImageAsset" },
                    { "ImageAltText", "ImageAltText" },
                }
            }
        },
        {
            "Download",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Download",
                KenticoClassName = "ContentBase.DownloadFile",
                FieldMappings = new Dictionary<string, string>
                {
                    { "SelectedFile", "DownloadAsset" },
                    { "ListingItemTitle", "ListingItemTitle" },
                }
            }
        },
        {
            "Video",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Video",
                KenticoClassName = "ContentBase.DownloadFile",
                FieldMappings = new Dictionary<string, string>
                {
                    { "SelectedFile", "DownloadAsset" },
                    { "ListingItemTitle", "ListingItemTitle" },
                }
            }
        },
        {
            "PageNode",
            new ContentTypeMapping
            {
                SitefinityTypeName = "PageNode",
                KenticoClassName = "ContentBase.ContentPage",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                }
            }
        },
        {
            "State",
            new ContentTypeMapping
            {
                SitefinityTypeName = "State",
                KenticoClassName = "ContentBase.ContentPage",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                }
            }
        },
        {
            "MagazineSponsor",
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
            "MagazineAuthor",
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
            "MagazineArticle",
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
                    { "PublicationAuthor", "AuthorByline" },
                    { "PublicationAuthorPages", "ArticleAuthor" },
                    { "PageImage", "HeroImage" },
                }
            }
        },
        {
            "MagazineIssue",
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
                }
            }
        },
        {
            "TaxManualItem",
            new ContentTypeMapping
            {
                SitefinityTypeName = "TaxManualItem",
                KenticoClassName = "Elfa.TaxManualItem",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "PageCopyHtml1", "FullText" },
                    { "TaxManualItemCategories", "taxmanualcategories" },
                    { "PublicationDate", "ReleaseDate" },
                    { "RelatedFilesDownloads", "Documents" },
                    { "PageImage", "Image" },
                    { "JurisdictionState", "State" }
                }
            }
        },
        {
            "CompendiumIssue",
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
                    { "JurisdictionState", "State" },
                    { "PublicationAuthor", "LastReviewAuthor" },
                    { "PublicationDate", "LastReviewDate" },
                    { "PublicationAuthorPages", "Authors" }
                }
            }
        },
        {
            "CompendiumAuthor",
            new ContentTypeMapping
            {
                SitefinityTypeName = "CompendiumAuthor",
                KenticoClassName = "ContentBase.PersonDetail",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "ContactPhoneOffice", "Phone" },
                    { "ContactEmail", "Email" },
                    { "PersonOrganization", "LawFirmName" },
                    { "PersonWebsiteUrl", "LawFirmWebsite" },
                }
            }
        },
        {
            "Program",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Program",
                KenticoClassName = "Elfa.EventProgram",
                FieldMappings = new Dictionary<string, string>
                {
                    { "ProgramTitle", "Title" },
                    { "ProgramStartTime", "StartDate" },
                    { "ProgramEndTime", "StartDate" },
                    { "ProgramSummary", "Summary" },
                }
            }
        },
        {
            "Mlfi",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Mlfi",
                KenticoClassName = "Elfa.Report",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "PageCopyHtml1", "Content" },
                    { "SeoFacebookImage", "OpenGraphImage" },
                }
            }
        },
        {
            "ElfaEvent",
            new ContentTypeMapping
            {
                SitefinityTypeName = "ElfaEvent",
                KenticoClassName = "ContentBase.EventDetail",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "EventExternalEventId", "CVDataEventId" },
                    { "EventEventPowerId", "EventPowerId" },
                    { "EventEventPowerKey", "EventPowerKey" },
                    { "EventRegistrationIsOpen", "RegistrationOpen" },
                    { "EventStartDate", "StartDate" },
                    { "EventEndDate", "EndDate" },
                    { "PageCopyHtml1", "Content" },
                    { "LocationName", "LocationName" },
                    { "LocationAddress1", "LocationAddress1" },
                    { "LocationAddress2", "LocationAddress2" },
                    { "LocationCity", "LocationCity" },
                    { "LocationState", "LocationState" },
                    { "LocationZip", "LocationPostalCode" },
                    { "EventSponsorCopyHtml", "SponsorContent" },
                    { "EventLocationCopyHtml", "LocationContent" },
                    { "EventScheduleCopyHtml", "ScheduleContent" },
                    { "EventSpeakerCopyHtml", "SpeakerContent" },
                    { "EventPolicyCopyHtml", "PolicyContent" },
                    { "EventExhibitorCopyHtml", "ExhibitorContent" },
                    { "PageImage", "Image" },
                    { "EventRegistrationLink", "CvEventURL" },
                }
            }
        },
        {
            "Event",
            new ContentTypeMapping
            {
                SitefinityTypeName = "Event",
                KenticoClassName = "ContentBase.EventDetail",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "EventStartDate", "EventStart" },
                    { "EventEndDate", "EventEnd" },
                    { "ContactEmail", "ContactEmail" },
                    { "ContactWebUrl", "ContactWeb" },
                    { "LocationAddress1", "Street" },
                    { "LocationCity", "City" },
                    { "LocationState", "State" },
                    { "ContactName", "ContactName" },
                    { "ContactPhoneCell", "ContactCell" },
                    { "ContactPhoneOffice", "ContactPhone" },
                    { "PageCopyHtml1", "Content" },
                    { "PageDescription", "Summary" },
                    { "ShowEventTime", "AllDayEvent" },
                    { "AdditionalCategories", "Category" },
                    { "LocationName", "LocationName" },
                    { "PageImage", "Image" },
                    { "EventRegistrationLink", "CvEventURL" },
                }
            }
        },
        {
            "NewsItem",
            new ContentTypeMapping
            {
                SitefinityTypeName = "NewsItem",
                KenticoClassName = "ContentBase.ArticleDetail",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "PageDescription", "Summary" },
                    { "PageCopyHtml1", "Content" },
                    { "PublicationAuthor", "Author" },
                    { "PublicationDate", "PublicationDate" },
                    { "SeoDisallowRobots", "IncludeInSitemap" },
                    { "AdditionalCategories", "Category" },
                    { "PageImage", "Image" },
                }
            }
        },
        {
            "FundingSourceProfile",
            new ContentTypeMapping
            {
                SitefinityTypeName = "FundingSourceProfile",
                KenticoClassName = "Elfa.FundingSourceProfile",
                FieldMappings = new Dictionary<string, string>
                {
                    { "PageTitle", "Title" },
                    { "FundSrcExternalId", "OrgId" },
                    { "PublicationDate", "LastUpdatedDate" },
                    { "FundSrcListingExpiration", "ListingExpirationDate" },
                    { "FundSrcCompanyType", "CvCompanyType" },
                    { "FundSrcExternalLogoId", "LogoId" },
                    { "FundSrcLeaseStructure", "LeaseStructure" },
                    { "FundSrcFundingProgram", "FundingProgram" },
                    { "FundSrcBusinessCouncils", "ElfaBusinessCouncils" },
                    { "FundSrcAnnualVolume", "AnnualVolume" },
                    { "FundSrcFundingCompanyType", "FundingSourceCompanyTypes" },
                    { "FundSrcBusinessFocus", "CoreBusinessFocus" },
                    { "FundSrcType", "FundingSourceTypes" },
                    { "FundSrcTransactionHighest", "IndTransactionHighest" },
                    { "FundSrcTransactionAverage", "IndTransactionAverage" },
                    { "FundSrcTransactionLowest", "IndTransactionLowest" },
                    { "FundSrcLeaseTermLongest", "LeaseTermLongest" },
                    { "FundSrcLeaseTermAverage", "LeaseTermAverage" },
                    { "FundSrcLeaseTermShortest", "LeaseTermShortest" },
                    { "FundSrcOriginatesPaper", "OriginatesPaper" },
                    { "FundSrcSyndicateSellPaper", "SyndicateSellPaper" },
                    { "FundSrcSyndicatePaperDetails", "SyndicatePaperDetails" },
                    { "FundSrcEquipmentTypes", "EquipmentTypes" },
                    { "FundSrcEquipmentTypesPreferred", "EquipmentTypesPreferred" },
                    { "FundSrcSyndicationsPortfolios", "SyndicationsPortfolios" },
                    { "FundSrcCreditCriteria", "CreditCriteria" },
                    { "FundSrcCreditCriteriaOther", "CreditCriteriaOther" },
                    { "FundSrcLenderType", "LenderType" },
                    { "FundSrcAcceptsSoftAssets", "AcceptsSoftAssets" },
                    { "FundSrcSoftAssetDescription", "SoftAssetDescription" },
                    { "FundSrcOtherRequirements", "OtherRequirements" },
                    { "FundSrcStartOfFiscalYear", "StartOfFiscalYear" },
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
        if (string.IsNullOrEmpty(sitefinityTypeName))
        {
            return originalClassName ?? string.Empty;
        }

        // Strip namespace from sitefinityTypeName before lookup
        string typeNameWithoutNamespace = StripNamespace(sitefinityTypeName);

        if (!contentTypeMappings.TryGetValue(typeNameWithoutNamespace, out var mapping))
        {
            return originalClassName ?? string.Empty;
        }

        logger.LogInformation("Mapping Sitefinity type '{SitefinityType}' to Kentico class '{KenticoClass}'",
            sitefinityTypeName, mapping.KenticoClassName);

        return mapping.KenticoClassName;
    }

    /// <summary>
    /// Gets all content type mapping keys from the content type mappings
    /// </summary>
    /// <returns>Collection of content type mapping keys</returns>
    public static IEnumerable<string> GetContentTypeMappingKeys() => contentTypeMappings.Keys;

    /// <summary>
    /// Gets all Kentico class names from the content type mappings
    /// </summary>
    /// <returns>Collection of Kentico class names</returns>
    public static IEnumerable<string> GetKenticoClassNames() => contentTypeMappings.Values.Select(mapping => mapping.KenticoClassName);

    /// <summary>
    /// Strips the namespace from a Sitefinity type name (e.g., "elfaold.TaxManualItem" becomes "TaxManualItem")
    /// </summary>
    /// <param name="sitefinityTypeName">The full Sitefinity type name with namespace</param>
    /// <returns>The type name without namespace</returns>
    private static string StripNamespace(string sitefinityTypeName)
    {
        if (string.IsNullOrEmpty(sitefinityTypeName))
        {
            return sitefinityTypeName;
        }

        int lastDotIndex = sitefinityTypeName.LastIndexOf('.');
        return lastDotIndex >= 0 ? sitefinityTypeName[(lastDotIndex + 1)..] : sitefinityTypeName;
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

        // For existing content types (like ContentBase.EventDetail), we need to find the original Sitefinity type
        // instead of trying to find a type with the existing Kentico content type GUID
        SitefinityType? type = null;

        // Check if this is a ContentItem with source data that has the original Sitefinity type information
        if (cultureSdkItem is ContentItem contentItem && !string.IsNullOrEmpty(contentItem.TypeName))
        {
            // Use the original Sitefinity DataClassGuid to find the type
            type = types.FirstOrDefault(x => x.Id == contentItem.DataClassGuid);

            if (type == null)
            {
                logger.LogDebug("Could not find Sitefinity type for content item using DataClassGuid {DataClassGuid}. Attempting to find by type name {TypeName}.",
                    contentItem.DataClassGuid, contentItem.TypeName);

                // Fallback: try to find by type name
                string typeNameWithoutNamespace = StripNamespace(contentItem.TypeName);
                type = types.FirstOrDefault(x => x.Name != null && x.Name.Equals(typeNameWithoutNamespace, StringComparison.OrdinalIgnoreCase));
            }
        }
        else
        {
            // Fallback to original behavior for other cases
            type = types.FirstOrDefault(x => x.Id == dataClassModel.ClassGUID);
        }

        if (type == null || type.Fields == null)
        {
            logger.LogWarning("Could not find Sitefinity type definition for content item {ItemUrl}. Cannot extract field data.", cultureSdkItem.UrlName);
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
        // For existing content types, we need to use the original Sitefinity type name instead of the Kentico class name
        string? sitefinityTypeName = null;

        if (cultureSdkItem is ContentItem sourceContentItem && !string.IsNullOrEmpty(sourceContentItem.TypeName))
        {
            // Use the original Sitefinity type name for mapping lookup
            sitefinityTypeName = sourceContentItem.TypeName;
        }
        else
        {
            // Fallback to using the dataClassModel class name for other cases
            sitefinityTypeName = dataClassModel.ClassName;
        }

        if (!string.IsNullOrEmpty(sitefinityTypeName))
        {
            // Strip namespace from sitefinityTypeName before lookup
            string typeNameWithoutNamespace = StripNamespace(sitefinityTypeName);
            if (contentTypeMappings.TryGetValue(typeNameWithoutNamespace, out var mapping))
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

                    // Special handling for MagazineArticle PageTitle to append Subhead
                    if (typeNameWithoutNamespace == "MagazineArticle" && sitefinityFieldName == "Title")
                    {
                        string titleValue = string.Empty;

                        // Get PageTitle value
                        if (contentItemData.TryGetValue("Title", out object? pageTitleValue) && pageTitleValue != null)
                        {
                            titleValue = pageTitleValue.ToString() ?? string.Empty;
                        }

                        // Get Subhead value and append if it exists
                        if (contentItemData.TryGetValue("Subhead", out object? subheadValue) &&
                            subheadValue != null &&
                            !string.IsNullOrWhiteSpace(subheadValue.ToString()))
                        {
                            string subheadText = subheadValue.ToString()!;
                            titleValue = string.IsNullOrWhiteSpace(titleValue)
                                ? subheadText
                                : $"{titleValue}: {subheadText}";
                        }

                        // Only add if we have a title value
                        if (!string.IsNullOrWhiteSpace(titleValue))
                        {
                            newContentItemData[kenticoFieldName] = titleValue;
                        }

                        continue; // Skip the normal field mapping logic for this field
                    }

                    // Special handling for ElfaEvent, Event, and NewsItem to combine Category and Tags into AdditionalCategories
                    if ((typeNameWithoutNamespace == "ElfaEvent" || typeNameWithoutNamespace == "Event" || typeNameWithoutNamespace == "NewsItem") && sitefinityFieldName == "Category")
                    {
                        string combinedCategories = string.Empty;

                        // Get Category value
                        if (contentItemData.TryGetValue("Category", out object? categoryValue) && categoryValue != null)
                        {
                            combinedCategories = categoryValue.ToString() ?? string.Empty;
                        }

                        // Get Tags value and append if it exists
                        if (contentItemData.TryGetValue("Tags", out object? tagsValue) &&
                            tagsValue != null &&
                            !string.IsNullOrWhiteSpace(tagsValue.ToString()) &&
                            !tagsValue.ToString()!.Equals("[]"))
                        {
                            string tagsText = tagsValue.ToString()!;

                            // If we have both Category and Tags, we need to merge the JSON arrays
                            if (!string.IsNullOrWhiteSpace(combinedCategories) && !combinedCategories.Equals("[]"))
                            {
                                combinedCategories = MergeTaxonomyArrays(combinedCategories, tagsText);
                            }
                            else
                            {
                                // If no Category data, just use Tags
                                combinedCategories = tagsText;
                            }
                        }

                        // Only add if we have category/tag data
                        if (!string.IsNullOrWhiteSpace(combinedCategories) && !combinedCategories.Equals("[]"))
                        {
                            newContentItemData[kenticoFieldName] = combinedCategories;
                        }

                        continue; // Skip the normal field mapping logic for this field
                    }

                    // Special handling for NewsItem SeoDisallowRobots to IncludeInSitemap (invert the boolean)
                    if (typeNameWithoutNamespace == "NewsItem" && sitefinityFieldName == "IncludeInSitemap")
                    {
                        if (contentItemData.TryGetValue("SeoDisallowRobots", out object? seoDisallowRobotsValue) && seoDisallowRobotsValue != null)
                        {
                            // Invert the SeoDisallowRobots value for IncludeInSitemap
                            bool disallowRobots = ValidationHelper.GetBoolean(seoDisallowRobotsValue, false);
                            bool includeInSitemap = !disallowRobots;
                            newContentItemData[kenticoFieldName] = includeInSitemap;
                        }
                        else
                        {
                            // Default to false if SeoDisallowRobots is not set
                            newContentItemData[kenticoFieldName] = false;
                        }

                        continue; // Skip the normal field mapping logic for this field
                    }

                    // Special handling for TaxManualItem PageImage to take only the first image from the array
                    if (typeNameWithoutNamespace == "TaxManualItem" && sitefinityFieldName == "Image")
                    {
                        if (contentItemData.TryGetValue("PageImage", out object? pageImageValue) && pageImageValue != null)
                        {
                            string pageImageJson = pageImageValue.ToString() ?? string.Empty;

                            // Try to parse as JSON array and take only the first item
                            try
                            {
                                var imageArray = JsonSerializer.Deserialize<List<ContentRelatedItem>>(pageImageJson);
                                if (imageArray != null && imageArray.Count > 0)
                                {
                                    // Create a new array with only the first image
                                    var firstImageArray = new List<ContentRelatedItem> { imageArray[0] };
                                    newContentItemData[kenticoFieldName] = JsonSerializer.Serialize(firstImageArray);
                                    logger.LogDebug("TaxManualItem PageImage: Selected first image from array of {Count} images", imageArray.Count);
                                }
                                else if (!string.IsNullOrWhiteSpace(pageImageJson) && !pageImageJson.Equals("[]"))
                                {
                                    // If it's not empty but couldn't parse as array, use as is
                                    newContentItemData[kenticoFieldName] = pageImageJson;
                                }
                            }
                            catch (JsonException)
                            {
                                // If JSON parsing fails, use the original value
                                if (!string.IsNullOrWhiteSpace(pageImageJson) && !pageImageJson.Equals("[]"))
                                {
                                    newContentItemData[kenticoFieldName] = pageImageJson;
                                }
                            }
                        }

                        continue; // Skip the normal field mapping logic for this field
                    }

                    // Special handling for ElfaEvent address fields to extract components from linked Address object
                    if (typeNameWithoutNamespace == "ElfaEvent" && (
                        kenticoFieldName == "LocationAddress1" ||
                        kenticoFieldName == "LocationAddress2" ||
                        kenticoFieldName == "LocationCity" ||
                        kenticoFieldName == "LocationState" ||
                        kenticoFieldName == "LocationZip"))
                    {
                        if (contentItemData.TryGetValue("Address", out object? addressValue) && addressValue != null)
                        {
                            try
                            {
                                // Try to parse the Address object from JSON
                                var addressData = JsonSerializer.Deserialize<JsonElement>(addressValue.ToString() ?? "");

                                if (addressData.ValueKind == JsonValueKind.Object)
                                {
                                    string? extractedValue = kenticoFieldName switch
                                    {
                                        "LocationAddress1" => addressData.TryGetProperty("Street", out var street) ? street.GetString() : null,
                                        "LocationAddress2" => null, // Address2 is typically not in the basic address object
                                        "LocationCity" => addressData.TryGetProperty("City", out var city) ? city.GetString() : null,
                                        "LocationState" => addressData.TryGetProperty("StateCode", out var state) ? state.GetString() : null,
                                        "LocationZip" => addressData.TryGetProperty("Zip", out var zip) ? zip.GetString() : null,
                                        _ => null
                                    };

                                    if (!string.IsNullOrWhiteSpace(extractedValue))
                                    {
                                        newContentItemData[kenticoFieldName] = extractedValue;
                                        logger.LogDebug("ElfaEvent Address: Extracted {Field} = {Value}", kenticoFieldName, extractedValue);
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                logger.LogWarning("Failed to parse Address object for ElfaEvent field {Field}: {Error}", kenticoFieldName, ex.Message);
                            }
                        }

                        continue; // Skip the normal field mapping logic for this field
                    }

                    // Special handling for Event ShowEventTime to AllDayEvent (invert the boolean)
                    if (typeNameWithoutNamespace == "Event" && sitefinityFieldName == "AllDayEvent")
                    {
                        if (contentItemData.TryGetValue("ShowEventTime", out object? showEventTimeValue) && showEventTimeValue != null)
                        {
                            // Invert the ShowEventTime value for AllDayEvent
                            bool showEventTime = ValidationHelper.GetBoolean(showEventTimeValue, false);
                            bool allDayEvent = !showEventTime;
                            newContentItemData[kenticoFieldName] = allDayEvent;
                        }
                        else
                        {
                            // Default to false if ShowEventTime is not set (meaning it's not an all-day event)
                            newContentItemData[kenticoFieldName] = false;
                        }

                        continue; // Skip the normal field mapping logic for this field
                    }

                    // Special handling for page reference fields to use WebPageGuid instead of Identifier
                    // Keep ONLY true page-reference fields here. Do NOT convert MagazineIssue -> Sponsors.
                    if ((typeNameWithoutNamespace == "MagazineArticle" && sitefinityFieldName == "ArticleAuthor") ||
                        (typeNameWithoutNamespace == "CompendiumIssue" && sitefinityFieldName == "Authors"))
                    {
                        if (contentItemData.TryGetValue(sitefinityFieldName, out object? pageReferencesValue) && pageReferencesValue != null)
                        {
                            string pageReferencesJson = pageReferencesValue.ToString() ?? string.Empty;

                            try
                            {
                                var pageReferences = JsonSerializer.Deserialize<List<ContentRelatedItem>>(pageReferencesJson);
                                if (pageReferences != null && pageReferences.Count > 0)
                                {
                                    // Convert from Identifier to WebPageGuid format for page references
                                    var webPageReferences = pageReferences
                                        .Where(item => item.Identifier != Guid.Empty)
                                        .Select(item => new { WebPageGuid = item.Identifier })
                                        .ToList();

                                    newContentItemData[kenticoFieldName] = JsonSerializer.Serialize(webPageReferences);
                                    logger.LogDebug("{ContentType} {FieldName}: Converted {Count} page references from Identifier to WebPageGuid format",
                                        typeNameWithoutNamespace, sitefinityFieldName, webPageReferences.Count);
                                }
                                else if (!string.IsNullOrWhiteSpace(pageReferencesJson) && !pageReferencesJson.Equals("[]", StringComparison.Ordinal))
                                {
                                    // If it's not empty but couldn't parse as array, use as is
                                    newContentItemData[kenticoFieldName] = pageReferencesJson;
                                }
                            }
                            catch (JsonException)
                            {
                                // If JSON parsing fails, use the original value
                                if (!string.IsNullOrWhiteSpace(pageReferencesJson) && !pageReferencesJson.Equals("[]", StringComparison.Ordinal))
                                {
                                    newContentItemData[kenticoFieldName] = pageReferencesJson;
                                }
                            }
                        }

                        continue; // Skip the normal field mapping logic for this field
                    }

                    // Map the field if it exists in the original data
                    if (contentItemData.TryGetValue(sitefinityFieldName, out object? fieldValue))
                    {
                        // Only add non-empty taxonomy data
                        if (fieldValue != null && !string.IsNullOrWhiteSpace(fieldValue.ToString()) && !fieldValue.ToString()!.Equals("[]"))
                        {
                            newContentItemData[kenticoFieldName] = fieldValue;
                        }
                    }
                }
            }
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

        // Only process relative URLs or URLs from the configured domain or production domain
        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            string? configuredDomain = dataConfiguration.SitefinitySiteDomain?.TrimEnd('/');
            string prodDomain = "www.elfaonline.org";
            string prodDomainWithoutWww = "elfaonline.org";

            // Extract only the host (e.g., "www.leasefoundation.org")
            string urlHost = absoluteUri.Host;

            // Check if it does NOT match the configured domain OR the production domains
            if (!urlHost.Equals(configuredDomain, StringComparison.OrdinalIgnoreCase) &&
                !urlHost.Equals(prodDomain, StringComparison.OrdinalIgnoreCase) &&
                !urlHost.Equals(prodDomainWithoutWww, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("URL {Url} does not belong to configured domain {Domain} or production domains. Skipping processing.", url, configuredDomain);
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
        // Define the asset field names and their corresponding GUIDs from TypeProvider
        string selectedImageFieldGuid = "e477a59e-1df6-4e2f-9986-20ab37342540";
        string selectedFileFieldGuid = "141d77fc-2e06-49eb-b14c-2ff58f5ce730"; // Updated to match TypeProvider GUID

        // Check for asset fields and return the corresponding GUID
        if (contentItemData.ContainsKey("SelectedImage"))
        {
            logger.LogDebug("Found asset field SelectedImage, returning GUID {FieldGuid}", selectedImageFieldGuid);
            return selectedImageFieldGuid;
        }

        if (contentItemData.ContainsKey("SelectedFile"))
        {
            logger.LogDebug("Found asset field SelectedFile, returning GUID {FieldGuid}", selectedFileFieldGuid);
            return selectedFileFieldGuid;
        }

        // Remove SelectedVideo check since videos now use SelectedFile

        return null;
    }

    /// <summary>
    /// Merges two taxonomy JSON arrays into a single array, removing duplicates based on Identifier
    /// </summary>
    /// <param name="array1">First JSON array of taxonomy items</param>
    /// <param name="array2">Second JSON array of taxonomy items</param>
    /// <returns>Merged JSON array with no duplicate Identifiers</returns>
    private static string MergeTaxonomyArrays(string array1, string array2)
    {
        try
        {
            var items1 = JsonSerializer.Deserialize<List<ContentRelatedItem>>(array1) ?? [];
            var items2 = JsonSerializer.Deserialize<List<ContentRelatedItem>>(array2) ?? [];

            // Combine and remove duplicates based on Identifier
            var mergedItems = items1
                .Concat(items2)
                .GroupBy(item => item.Identifier)
                .Select(group => group.First())
                .ToList();

            return JsonSerializer.Serialize(mergedItems);
        }
        catch (JsonException)
        {
            // If JSON parsing fails, return the first array as fallback
            return array1;
        }
    }

    private static ContentItemSimplifiedModel? FindMediaFileByUrl(IMediaDependencies mediaDependencies, string targetUrl) => mediaDependencies.MediaFiles.Values.FirstOrDefault(contentItem =>
        // Check all language variants for a matching asset URL
        (contentItem.LanguageData ?? [])
            .Select(languageData => languageData.ContentItemData)
            .Where(contentItemData => contentItemData != null)
            .Any(contentItemData =>
            {
                // Check for various asset URL field names based on content type
                string[] assetUrlFields = new[] { "ImageAssetLegacyUrl", "DownloadAssetLegacyUrl" };

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
