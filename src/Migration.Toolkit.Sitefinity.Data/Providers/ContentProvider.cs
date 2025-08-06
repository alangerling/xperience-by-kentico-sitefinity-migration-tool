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
    // Replace the tuple declaration with a single-element record or struct, or use a string array if only one value is needed.
    // Here, a string array is sufficient since only the Name is used.
    // Leave empty to not filter by type
    private static readonly string[] allowedTypes = {
        //"ElfaEvent",
        //"Program",
        //"MagazineIssue",
        //"MagazineAuthor",
        //"MagazineArticle",
        //"MagazineSponsor",
        //"TaxManualItem",
        //"State",
        //"CompendiumIssue",
        //"CompendiumAuthor",
    };

    private IEnumerable<SitefinityVersionChange>? versions;
    private IEnumerable<SitefinityPageNode>? pageNodes;

    public IEnumerable<ContentItem> GetContentItems(IEnumerable<SitefinityTypeDefinition> typeDefinitions, IEnumerable<SystemCulture> cultures)
    {
        // Filter typeDefinitions to only allowed types
        var filteredTypeDefinitions = typeDefinitions
            .Where(td => allowedTypes.Contains(td.SitefinityTypeName))
            .ToList();
        if (true || !filteredTypeDefinitions.Any())
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

    private Dictionary<Guid, ContentItem> GetContentItemsInternal(IEnumerable<SitefinityTypeDefinition> typeDefinitions, SystemCulture defaultCulture)
    {
        var contentItems = new Dictionary<Guid, ContentItem>();

        foreach (var typeDefinition in typeDefinitions)
        {
            var getAllArgs = new GetAllArgs
            {
                Type = $"{typeDefinition.SitefinityTypeNameSpace}.{typeDefinition.SitefinityTypeName}",
                Fields = ["*"],
                Culture = defaultCulture.Culture
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
