using Kentico.Xperience.UMT.Model;

using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Model;

namespace Migration.Toolkit.Sitefinity.Core.Helpers;
/// <summary>
/// Helper class for all content related operations.
/// </summary>
public interface IContentHelper
{
    /// <summary>
    /// Gets the language data for the given culture.
    /// </summary>
    /// <param name="contentDependencies">Content dependencies used for content item or web page import.</param>
    /// <param name="cultureSdkItem">Content item or web page in a particular culture.</param>
    /// <param name="dataClassModel">Dataclass used for content item or web page.</param>
    /// <param name="createdByUser">User who created content item or web page.</param>
    /// <returns>All language data for content item or web page.</returns>
    public IEnumerable<ContentItemLanguageData> GetLanguageData(ContentDependencies contentDependencies, ICultureSdkItem cultureSdkItem, DataClassModel dataClassModel, UserInfoModel? createdByUser);

    /// <summary>
    /// Gets the mapped Kentico class name for a given Sitefinity type name, or returns the original if no mapping exists.
    /// </summary>
    /// <param name="sitefinityTypeName">The Sitefinity type name</param>
    /// <param name="originalClassName">The original class name from DataClassModel</param>
    /// <returns>The mapped Kentico class name or the original if no mapping exists</returns>
    public string GetMappedClassName(string? sitefinityTypeName, string? originalClassName);

    /// <summary>
    /// Gets name using title and guid. Default length is 100.
    /// </summary>
    /// <param name="title">Title of content item.</param>
    /// <param name="id">Unique identier of content item.</param>
    /// <param name="length">Optional length to trim name.</param>
    /// <returns>Processed name.</returns>
    public string GetName(string title, Guid id, int length = 100);

    /// <summary>
    /// Gets the current site based on domain set in settings.
    /// </summary>
    /// <returns>Current site.</returns>
    public Site? GetCurrentSite();

    /// <summary>
    /// Gets the current channel to be imported based on current site.
    /// </summary>
    /// <param name="channels">Channels to be imported.</param>
    /// <returns>Current channel.</returns>
    public ChannelModel? GetCurrentChannel(IEnumerable<ChannelModel> channels);

    /// <summary>
    /// Gets page urls for the given content dependencies and source. Optional root path and page path.
    /// </summary>
    /// <param name="dependenciesModel">Content dependencies used for content item or web page import.</param>
    /// <param name="source">Content item or web page in a particular culture.</param>
    /// <param name="rootPath">Optional root path to be appended in front of content item's url.</param>
    /// <param name="pagePath">Optional page path to manually override the url of the page.</param>
    /// <returns>List of page urls.</returns>
    public List<PageUrlModel> GetPageUrls(ContentDependencies dependenciesModel, ICultureSdkItem source, string? rootPath = null, string? pagePath = null);

    /// <summary>
    /// Gets parent path based on the given path.
    /// </summary>
    /// <param name="path">Path to page.</param>
    /// <returns>Parent path.</returns>
    public string GetParentPath(string? path);

    /// <summary>
    /// Removes path segments from the start of the given path.
    /// </summary>
    /// <param name="path">Path to page.</param>
    /// <param name="numberOfSegments">Amount of segments to remove from path.</param>
    /// <returns>Updated path.</returns>
    public string RemovePathSegmentsFromStart(string path, int numberOfSegments);

    /// <summary>
    /// Makes absolute urls as relative. Handles both absolute and relative urls.
    /// </summary>
    /// <param name="url">Absolute or relatvie url.</param>
    /// <returns>Relative url.</returns>
    public string GetRelativeUrl(string url);

    /// <summary>
    /// Updates all image and link urls to the media's permanent urls.
    /// </summary>
    /// <param name="mediaDependencies">Required media files to process urls.</param>
    /// <param name="html">Html to be processed.</param>
    /// <returns>Html with media permanent urls where applicable.</returns>
    public string UpdateUrlsToPermanent(IMediaDependencies mediaDependencies, string html);

    /// <summary>
    /// Truncates the last URL segment if it exceeds the max length and adds a former URL entry per culture. Also adjusts the TreePath accordingly.
    /// </summary>
    /// <param name="pageData">Page data to update.</param>
    /// <param name="maxSlugLength">Maximum allowed length for the last slug (default 50).</param>
    public void EnforceMaxSlugLength(PageDataModel? pageData, int maxSlugLength = 50);
}
