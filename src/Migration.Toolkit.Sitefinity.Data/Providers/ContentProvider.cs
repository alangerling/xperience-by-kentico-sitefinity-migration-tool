using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Migration.Toolkit.Data.Abstractions;
using Migration.Toolkit.Data.Core.EF;
using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;

using Progress.Sitefinity.RestSdk;

namespace Migration.Toolkit.Data.Providers;
internal class ContentProvider(IRestClient restClient, ILogger<ContentProvider> logger, IDbContextFactory<SitefinityContext> sitefinityContext) : RestSdkBase(restClient), IContentProvider
{

    private static readonly string[] allowedTypes = {
        "NewsItem",
        "Event",
        "ElfaEvent",
        "Mlfi",
        "Program",
        "MagazineIssue",
        "MagazineAuthor",
        "MagazineArticle",
        "MagazineSponsor",
        "TaxManualItem",
        "State",
        "CompendiumIssue",
        "CompendiumAuthor",
        "FundingSourceProfile",
    };

    private IEnumerable<SitefinityVersionChange>? versions;
    private IEnumerable<SitefinityPageNode>? pageNodes;

    public IEnumerable<ContentItem> GetContentItems(IEnumerable<SitefinityTypeDefinition> typeDefinitions, IEnumerable<SystemCulture> cultures)
    {
        // Filter typeDefinitions to only allowed types
        var filteredTypeDefinitions = typeDefinitions
            .Where(td => allowedTypes.Contains(td.SitefinityTypeName))
            .ToList();
        if (!filteredTypeDefinitions.Any())
        {
            filteredTypeDefinitions = (List<SitefinityTypeDefinition>)typeDefinitions;
        }

        using var context = sitefinityContext.CreateDbContext();
        versions ??= [.. context.VersionChanges.OrderByDescending(x => x.Version).Where(x => x.ChangeType.Equals("publish"))];

        var defaultCulture = cultures.FirstOrDefault(cultures => cultures.IsDefault);

        if (defaultCulture == null || defaultCulture.Culture == null)
        {
            logger.LogCritical("Default culture not found. Cannot retrieve content items from Sitefinity.");
            return [];
        }

        var contentItems = GetContentItemsInternal(filteredTypeDefinitions, defaultCulture);

        foreach (var alternateCulture in cultures.Where(x => !defaultCulture.Culture.Equals(x.Culture)))
        {
            var alternateCultureContentItems = GetContentItemsInternal(filteredTypeDefinitions, alternateCulture);

            foreach (var alternateContentItem in alternateCultureContentItems)
            {
                if (!contentItems.TryGetValue(alternateContentItem.Key, out var contentItem))
                {
                    continue;
                }

                contentItem.AlternateLanguageContentItems.Add(alternateContentItem.Value);
            }
        }

        return contentItems.Values;
    }
    public IEnumerable<ContentItem> GetProgramsContentItems(IEnumerable<SitefinityTypeDefinition> typeDefinitions, IEnumerable<SystemCulture> cultures)
    {
        string[] allowedTypes = new[]
        {
            "Program",
        };

        // Filter typeDefinitions to only allowed types
        var filteredTypeDefinitions = typeDefinitions
            .Where(td => allowedTypes.Contains(td.SitefinityTypeName))
            .ToList();

        if (!filteredTypeDefinitions.Any())
        {
            filteredTypeDefinitions = (List<SitefinityTypeDefinition>)typeDefinitions;
        }

        using var context = sitefinityContext.CreateDbContext();
        versions ??= [.. context.VersionChanges.OrderByDescending(x => x.Version).Where(x => x.ChangeType.Equals("publish"))];

        var defaultCulture = cultures.FirstOrDefault(cultures => cultures.IsDefault);

        if (defaultCulture == null || defaultCulture.Culture == null)
        {
            logger.LogCritical("Default culture not found. Cannot retrieve content items from Sitefinity.");
            return [];
        }

        var contentItems = GetContentItemsInternal(filteredTypeDefinitions, defaultCulture);

        foreach (var alternateCulture in cultures.Where(x => !defaultCulture.Culture.Equals(x.Culture)))
        {
            var alternateCultureContentItems = GetContentItemsInternal(filteredTypeDefinitions, alternateCulture);

            foreach (var alternateContentItem in alternateCultureContentItems)
            {
                if (!contentItems.TryGetValue(alternateContentItem.Key, out var contentItem))
                {
                    continue;
                }

                contentItem.AlternateLanguageContentItems.Add(alternateContentItem.Value);
            }
        }

        return contentItems.Values;
    }

    /// <summary>
    /// Gets filtered NewsItem content based on ELFA business rules.
    /// Filters for status = 2 (published), specific organizations, and specific email domains.
    /// </summary>
    /// <param name="typeDefinitions">Type definitions to query</param>
    /// <param name="cultures">Cultures to query</param>
    /// <returns>Filtered NewsItem content</returns>
    public IEnumerable<ContentItem> GetFilteredNewsItems(IEnumerable<SitefinityTypeDefinition> typeDefinitions, IEnumerable<SystemCulture> cultures)
    {
        string[] allowedTypes = new[] { "NewsItem" };

        // Filter typeDefinitions to only NewsItem types
        var filteredTypeDefinitions = typeDefinitions
            .Where(td => allowedTypes.Contains(td.SitefinityTypeName))
            .ToList();

        if (!filteredTypeDefinitions.Any())
        {
            logger.LogWarning("No NewsItem type definitions found for filtering.");
            return [];
        }

        using var context = sitefinityContext.CreateDbContext();
        versions ??= [.. context.VersionChanges.OrderByDescending(x => x.Version).Where(x => x.ChangeType.Equals("publish"))];

        var defaultCulture = cultures.FirstOrDefault(cultures => cultures.IsDefault);

        if (defaultCulture == null || defaultCulture.Culture == null)
        {
            logger.LogCritical("Default culture not found. Cannot retrieve NewsItem content from Sitefinity.");
            return [];
        }

        var contentItems = GetContentItemsInternal(filteredTypeDefinitions, defaultCulture);

        // Apply ELFA business rule filtering
        var filteredContentItems = new Dictionary<Guid, ContentItem>();

        foreach (var kvp in contentItems)
        {
            var item = kvp.Value;

            // Apply the same filtering logic as the SQL query
            if (IsElfaNewsItem(item))
            {
                filteredContentItems.Add(kvp.Key, item);
                logger.LogDebug("NewsItem {ItemId} ({ItemTitle}) passed ELFA filtering criteria.", item.Id, item.Title);
            }
            else
            {
                logger.LogDebug("NewsItem {ItemId} ({ItemTitle}) filtered out by ELFA criteria.", item.Id, item.Title);
            }
        }

        // Handle alternate cultures for filtered items
        foreach (var alternateCulture in cultures.Where(x => !defaultCulture.Culture.Equals(x.Culture)))
        {
            var alternateCultureContentItems = GetContentItemsInternal(filteredTypeDefinitions, alternateCulture);

            foreach (var alternateContentItem in alternateCultureContentItems)
            {
                if (!filteredContentItems.TryGetValue(alternateContentItem.Key, out var contentItem))
                {
                    continue;
                }

                contentItem.AlternateLanguageContentItems.Add(alternateContentItem.Value);
            }
        }

        logger.LogInformation("Filtered NewsItems: {FilteredCount} out of {TotalCount} items passed ELFA criteria.",
            filteredContentItems.Count, contentItems.Count);

        return filteredContentItems.Values;
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
        // Note: Not filtering by related_u_r_ls as per the commented SQL

        bool passes = organizationMatches && emailMatches;

        if (!passes)
        {
            logger.LogTrace("NewsItem {ItemId} filtered out. Organization: '{Organization}' (matches: {OrgMatches}), Email: '{Email}' (matches: {EmailMatches})",
                newsItem.Id, organization ?? "null", organizationMatches, email ?? "null", emailMatches);
        }

        return passes;
    }

    private Dictionary<Guid, ContentItem> GetContentItemsInternal(IEnumerable<SitefinityTypeDefinition> typeDefinitions, SystemCulture defaultCulture)
    {
        var contentItems = new Dictionary<Guid, ContentItem>();

        foreach (var typeDefinition in typeDefinitions)
        {
            var getAllArgs = new GetAllArgs
            {
                Type = $"{typeDefinition.SitefinityTypeNameSpace}.{typeDefinition.SitefinityTypeName}",
                Fields = ["*"],
                Culture = defaultCulture.Culture,
            };

            var items = GetUsingBatches<ContentItem>(getAllArgs);
            var adminGuid = Guid.Parse("6415B8CE-8072-4BCD-8E48-9D7178B826B7");

            foreach (var item in items)
            {
                item.DataClassGuid = typeDefinition.DataClassGuid;
                item.TypeName = typeDefinition.SitefinityTypeName;
                item.Culture = defaultCulture.Culture;
                if (versions != null)
                {
                    var version = versions.FirstOrDefault(x => x.ItemId == item.Id);

                    if (version != null)
                    {
                        // Use admin GUID if Owner is invalid
                        item.Owner = version.Owner == Guid.Empty ? adminGuid : version.Owner;
                        item.ChangeType = version.ChangeType;
                    }
                    else
                    {
                        // Use admin GUID if Owner is invalid
                        item.Owner = item.Owner == Guid.Empty ? adminGuid : item.Owner;
                    }
                }
                else
                {
                    // Use admin GUID if Owner is invalid
                    item.Owner = item.Owner == Guid.Empty ? adminGuid : item.Owner;
                }

                contentItems.Add(item.Id, item);
            }
        }

        return contentItems;
    }

    public IEnumerable<Page> GetPages(IEnumerable<SystemCulture> cultures)
    {
        using var context = sitefinityContext.CreateDbContext();
        pageNodes ??= [.. context.PageNodes];

        var defaultCulture = cultures.FirstOrDefault(cultures => cultures.IsDefault);

        if (defaultCulture == null || defaultCulture.Culture == null)
        {
            logger.LogCritical("Default culture not found. Cannot retrieve content items from Sitefinity.");
            return [];
        }

        var pages = GetPagesInternal(defaultCulture);

        foreach (var alternateCulture in cultures.Where(x => !defaultCulture.Culture.Equals(x.Culture)))
        {
            var alternateCultureContentItems = GetPagesInternal(alternateCulture);

            foreach (var alternateContentItem in alternateCultureContentItems)
            {
                if (!pages.TryGetValue(alternateContentItem.Key, out var contentItem))
                {
                    continue;
                }

                contentItem.AlternateLanguageContentItems.Add(alternateContentItem.Value);
            }
        }

        return pages.Values;
    }

    public IEnumerable<Page> GetPages(IEnumerable<SystemCulture> cultures, IEnumerable<string>? requiredPaths)
    {
        var pages = GetPages(cultures).ToDictionary(p => p.Id);

        var adminGuid = Guid.Parse("6415B8CE-8072-4BCD-8E48-9D7178B826B7");
        var enCulture = cultures.FirstOrDefault(c => c.Culture == "en");
        var templatePage = pages.Values.FirstOrDefault();

        if (requiredPaths != null && enCulture != null && templatePage != null)
        {
            // Sort requiredPaths by length (shortest first)
            var sortedRequiredPaths = requiredPaths.OrderBy(p => p.Trim('/').Length).ToList();

            // Map from path to page Id for parent lookup
            var pathToPageId = pages.Values
                .Where(p => !string.IsNullOrEmpty(p.RelativeUrlPath))
                .ToDictionary(p => p.RelativeUrlPath!.Trim('/'), p => p.Id, StringComparer.OrdinalIgnoreCase);

            foreach (string fullPath in sortedRequiredPaths)
            {
                string[] segments = fullPath.Trim('/').Split('/');
                string currentPath = "";
                Guid? parentId = null;

                for (int i = 0; i < segments.Length; i++)
                {
                    currentPath = i == 0 ? segments[0] : $"{currentPath}/{segments[i]}";
                    string normalizedPath = "/" + currentPath;

                    // Check if this page already exists
                    var existingPage = pages.Values.FirstOrDefault(p =>
                        p.RelativeUrlPath != null &&
                        p.RelativeUrlPath.Trim('/').Equals(currentPath, StringComparison.OrdinalIgnoreCase));

                    if (existingPage == null)
                    {
                        // Create a new page for this segment
                        var newPage = new Page
                        {
                            Id = Guid.NewGuid(),
                            RelativeUrlPath = normalizedPath,
                            Owner = adminGuid,
                            DateCreated = DateTime.UtcNow,
                            Culture = enCulture.Culture,
                            AlternateLanguageContentItems = [],
                            Title = segments[i],
                            UrlName = segments[i],
                            ShowInNavigation = "false",
                            Provider = "OpenAccessDataProvider"
                        };

                        // Set parent if available
                        if (parentId.HasValue)
                        {
                            newPage.ParentId = parentId.Value.ToString();
                        }

                        pages.Add(newPage.Id, newPage);
                        pathToPageId[currentPath] = newPage.Id;
                        parentId = newPage.Id;

                        logger.LogInformation("Created missing page for path segment: {Path}", normalizedPath);
                    }
                    else
                    {
                        parentId = existingPage.Id;
                    }
                }
            }
        }

        return pages.Values;
    }

    private Dictionary<Guid, Page> GetPagesInternal(SystemCulture culture)
    {
        var pages = new Dictionary<Guid, Page>();
        var getAllArgs = new GetAllArgs
        {
            Type = RestClientContentTypes.Pages,
            Culture = culture.Culture
        };

        var sitefinityPages = GetUsingBatches<Page>(getAllArgs);

        foreach (var page in sitefinityPages)
        {
            if (pageNodes != null)
            {
                var pageNode = pageNodes.FirstOrDefault(x => x.Id == page.Id);

                if (pageNode != null)
                {
                    page.Owner = pageNode.Owner;
                }
            }

            page.Culture = culture.Culture;

            pages.Add(page.Id, page);
        }

        return pages;
    }
}
