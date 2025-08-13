using Migration.Toolkit.Data.Models;

namespace Migration.Toolkit.Data.Core.Providers;
/// <summary>
/// Provider for getting content items and pages from Sitefinity using Rest Sdk.
/// </summary>
public interface IContentProvider
{
    /// <summary>
    /// Gets Pages from Sitefinity.
    /// </summary>
    /// <param name="cultures">Cultures to query.</param>
    /// <returns>Pages from Sitefinity.</returns>
    public IEnumerable<Page> GetPages(IEnumerable<SystemCulture> cultures);

    /// <summary>
    /// Gets Pages from Sitefinity.
    /// </summary>
    /// <param name="cultures">Cultures to query.</param>
    /// <returns>Pages from Sitefinity.</returns>
    public IEnumerable<Page> GetPages(IEnumerable<SystemCulture> cultures, IEnumerable<string>? requiredPaths);

    /// <summary>
    /// Gets static and dynamic module Content Items from Sitefinity.
    /// </summary>
    /// <param name="typeDefinitions">All type definitions to query.</param>
    /// <param name="cultures">Cultures to query.</param>
    /// <returns>Static and dynamic module Content Items from Sitefinity.</returns>
    public IEnumerable<ContentItem> GetContentItems(IEnumerable<SitefinityTypeDefinition> typeDefinitions, IEnumerable<SystemCulture> cultures);

    /// <summary>
    /// Gets Program content items from Sitefinity.
    /// </summary>
    /// <param name="typeDefinitions">Type definitions to query.</param>
    /// <param name="cultures">Cultures to query.</param>
    /// <returns>Program content items from Sitefinity.</returns>
    public IEnumerable<ContentItem> GetProgramsContentItems(IEnumerable<SitefinityTypeDefinition> typeDefinitions, IEnumerable<SystemCulture> cultures);

    /// <summary>
    /// Gets filtered NewsItem content based on ELFA business rules.
    /// Filters for status = 2 (published), specific organizations, and specific email domains.
    /// </summary>
    /// <param name="typeDefinitions">Type definitions to query.</param>
    /// <param name="cultures">Cultures to query.</param>
    /// <returns>Filtered NewsItem content that meets ELFA criteria.</returns>
    public IEnumerable<ContentItem> GetFilteredNewsItems(IEnumerable<SitefinityTypeDefinition> typeDefinitions, IEnumerable<SystemCulture> cultures);
}
