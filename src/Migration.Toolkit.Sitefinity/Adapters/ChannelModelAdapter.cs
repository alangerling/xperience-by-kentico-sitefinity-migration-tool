using CMS.ContentEngine;
using CMS.Helpers;

using Kentico.Xperience.UMT.Model;

using Microsoft.Extensions.Logging;

using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Abstractions;
using Migration.Toolkit.Sitefinity.Model;

namespace Migration.Toolkit.Sitefinity.Adapters;
internal class ChannelModelAdapter(ILogger<ChannelModelAdapter> logger) : UmtAdapterBaseWithDependencies<Site, ChannelDependencies>(logger)
{
    protected override IEnumerable<IUmtModel>? AdaptInternal(Site source, ChannelDependencies channelDependencies)
    {
        var siteDefaultLanguage = source.SystemCultures?.FirstOrDefault(x => x.IsDefault);

        if (siteDefaultLanguage == null)
        {
            logger.LogWarning("Default language not found for site {SiteName}", source.Name);
            yield break;
        }

        var language = channelDependencies.ContentLanguages.Values.FirstOrDefault(x => x.ContentLanguageCultureFormat == siteDefaultLanguage.Culture);

        if (language == null)
        {
            logger.LogWarning("Imported language not found for site {SiteName}", source.Name);
            yield break;
        }

        // Validate site name before creating channel
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            logger.LogError("Site name is null or empty for site {SiteId}. Cannot create channel.", source.Id);
            yield break;
        }

        string channelName = ValidationHelper.GetCodeName(source.Name).Replace(".", "-");

        // Validate generated channel name
        if (string.IsNullOrWhiteSpace(channelName))
        {
            logger.LogError("Generated channel name is null or empty for site '{SiteName}' (ID: {SiteId}). Cannot create channel.", source.Name, source.Id);
            yield break;
        }

        var channel = new ChannelModel
        {
            ChannelDisplayName = source.Name,
            ChannelName = channelName,
            ChannelGUID = source.Id,
            ChannelType = ChannelType.Website,
        };

        logger.LogInformation("Created channel '{ChannelName}' for site '{SiteName}' (ID: {SiteId})", channelName, source.Name, source.Id);

        yield return channel;

        var websiteChannel = new WebsiteChannelModel
        {
            WebsiteChannelChannelGuid = source.Id,
            WebsiteChannelGUID = source.Id,
            WebsiteChannelDefaultCookieLevel = 1000,
            WebsiteChannelDomain = source.LiveUrl,
            WebsiteChannelHomePage = "/home",
            WebsiteChannelPrimaryContentLanguageGuid = language.ContentLanguageGUID,
            WebsiteChannelStoreFormerUrls = false
        };

        yield return websiteChannel;
    }

}
