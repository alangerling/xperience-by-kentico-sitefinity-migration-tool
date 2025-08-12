namespace Migration.Toolkit.Sitefinity.Configuration
{
    /// <summary>
    /// Configuration for importing objects and content into XbyK site from Sitefinity.
    /// </summary>
    public class SitefinityImportConfiguration
    {
        /// <summary>
        /// Prefix used in XbyK code names.
        /// </summary>
        public required string SitefinityCodeNamePrefix { get; set; }
        /// <summary>
        /// Used to translate Sitefinity auto routing pages into physical child pages in XbyK.
        /// </summary>
        public IEnumerable<PageContentType>? PageContentTypes { get; set; }
        /// <summary>
        /// Kentico workspace name used when importing content items.
        /// </summary>
        public required string KenticoDefaultWorkspaceName { get; set; }
        /// <summary>
        /// Kentico administrator user name to use as fallback when Sitefinity user is not found.
        /// </summary>
        public required string KenticoAdministratorUserName { get; set; }

        /// <summary>
        /// Optional domain to use for downloading media files. If not specified, uses SitefinitySiteDomain.
        /// </summary>
        public string? MediaDownloadDomain { get; set; }

        /// <summary>
        /// GUID of the Sitefinity admin user. Used to identify admin-submitted content vs member-submitted content.
        /// Defaults to "6415B8CE-8072-4BCD-8E48-9D7178B826B7" if not specified.
        /// </summary>
        public string? SitefinityAdminUserGuid { get; set; }
    }
}
