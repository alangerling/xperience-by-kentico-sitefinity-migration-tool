using Kentico.Xperience.UMT.Model;

namespace Migration.Toolkit.Sitefinity.Services;

/// <summary>
/// Service for mapping Sitefinity content types to existing Kentico content types.
/// This service provides GUIDs for existing content types in the target Kentico database
/// to avoid creating new ones during migration.
/// </summary>
internal class ExistingContentTypeMappingService : IExistingContentTypeMappingService
{
    /// <summary>
    /// Mapping of Sitefinity content type names to existing Kentico content type information.
    /// Key: Sitefinity type name (without namespace)
    /// Value: Existing Kentico content type details
    /// </summary>
    private static readonly Dictionary<string, ExistingContentTypeInfo> existingContentTypeMappings = new()
    {
        {
            "ElfaEvent",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("4236BF8E-5343-445A-8F04-3FB9F8E623F0"), // ContentBase.EventDetail
                ClassName = "ContentBase.EventDetail",
                ClassContentTypeType = "Website"
            }
        },
        {
            "Event",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("4236BF8E-5343-445A-8F04-3FB9F8E623F0"), // ContentBase.EventDetail
                ClassName = "ContentBase.EventDetail",
                ClassContentTypeType = "Website"
            }
        },
        {
            "NewsItem",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("EDCC34AB-FB98-49BF-9EB9-59E2683BEA4C"), // ContentBase.ArticleDetail
                ClassName = "ContentBase.ArticleDetail",
                ClassContentTypeType = "Website"
            }
        },
        {
            "MagazineArticle",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("EDCC34AB-FB98-49BF-9EB9-59E2683BEA4C"), // ContentBase.ArticleDetail
                ClassName = "ContentBase.ArticleDetail",
                ClassContentTypeType = "Website"
            }
        },
        {
            "FundingSourceProfile",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("941E4ACE-6C4C-4AA4-9691-E2293EC7C6C5"), // Elfa.FundingSourceProfile
                ClassName = "Elfa.FundingSourceProfile",
                ClassContentTypeType = "Website"
            }
        },
        {
            "CompendiumIssue",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("E8E1B109-50B0-440D-BD51-C84BA1F14063"), // Elfa.CompendiumIssue
                ClassName = "Elfa.CompendiumIssue",
                ClassContentTypeType = "Website"
            }
        },
        {
            "TaxManualItem",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("E9118DDA-01F1-4014-936C-5726697290A5"), // Elfa.TaxManualItem
                ClassName = "Elfa.TaxManualItem",
                ClassContentTypeType = "Website"
            }
        },
        {
            "Program",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("9EAA8C86-38E6-4AAC-90E3-CF83329E40C5"), // Elfa.EventProgram
                ClassName = "Elfa.EventProgram",
                ClassContentTypeType = "Reusable"
            }
        },
        {
            "MagazineAuthor",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("CEE1D69B-66F1-45CB-8FEC-FDD40CB80E9C"), // ContentBase.PersonDetail
                ClassName = "ContentBase.PersonDetail",
                ClassContentTypeType = "Website"
            }
        },
        {
            "CompendiumAuthor",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("CEE1D69B-66F1-45CB-8FEC-FDD40CB80E9C"), // ContentBase.PersonDetail
                ClassName = "ContentBase.PersonDetail",
                ClassContentTypeType = "Website"
            }
        },
        {
            "MagazineSponsor",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("76050466-B689-448D-9DDA-4403C756817C"), // Elfa.Organization
                ClassName = "Elfa.Organization",
                ClassContentTypeType = "Reusable"
            }
        },
        {
            "PageNode",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("4B4CA446-033B-4C89-B02E-6B8CFBA378C3"), // ContentBase.ContentPage
                ClassName = "ContentBase.ContentPage",
                ClassContentTypeType = "Website"
            }
        },
        {
            "State",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("4B4CA446-033B-4C89-B02E-6B8CFBA378C3"), // ContentBase.ContentPage
                ClassName = "ContentBase.ContentPage",
                ClassContentTypeType = "Website"
            }
        },
        {
            "MagazineIssue",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("EC493603-893E-44F4-A492-F76A36E3D804"), // Elfa.MagazineIssue
                ClassName = "Elfa.MagazineIssue",
                ClassContentTypeType = "Website"
            }
        },
        {
            "Mlfi",
            new ExistingContentTypeInfo
            {
                ClassGUID = Guid.Parse("BD571797-B1F1-4640-B681-618E26F9D93D"), // ContentBase.Report
                ClassName = "ContentBase.Report",
                ClassContentTypeType = "Website"
            }
        }
    };

    /// <summary>
    /// Gets the existing Kentico content type information for a given Sitefinity type name.
    /// </summary>
    /// <param name="sitefinityTypeName">The Sitefinity content type name</param>
    /// <returns>Existing content type information if found, null otherwise</returns>
    public ExistingContentTypeInfo? GetExistingContentType(string? sitefinityTypeName)
    {
        if (string.IsNullOrEmpty(sitefinityTypeName))
        {
            return null;
        }

        // Strip namespace from sitefinityTypeName before lookup
        string typeNameWithoutNamespace = StripNamespace(sitefinityTypeName);

        return existingContentTypeMappings.TryGetValue(typeNameWithoutNamespace, out var mapping)
            ? mapping
            : null;
    }

    /// <summary>
    /// Creates a DataClassModel from existing content type information.
    /// This allows the migration to use existing content types without importing new ones.
    /// </summary>
    /// <param name="existingContentType">Existing content type information</param>
    /// <returns>DataClassModel representing the existing content type</returns>
    public DataClassModel CreateDataClassModelFromExisting(ExistingContentTypeInfo existingContentType) => new()
    {
        ClassGUID = existingContentType.ClassGUID,
        ClassName = existingContentType.ClassName,
        ClassContentTypeType = existingContentType.ClassContentTypeType,
        ClassDisplayName = existingContentType.ClassName,
        ClassShortName = existingContentType.ClassName,
        ClassTableName = existingContentType.ClassName.Replace(".", "_"),
        ClassType = "Content",
        Fields = [], // No fields needed for existing types
        ClassLastModified = DateTime.Now,
        ClassHasUnmanagedDbSchema = false,
        ClassResourceGuid = null,
        ClassWebPageHasUrl = existingContentType.ClassContentTypeType == "Website"
    };

    /// <summary>
    /// Gets all supported Sitefinity type names that have existing Kentico mappings.
    /// </summary>
    /// <returns>Collection of supported Sitefinity type names</returns>
    public IEnumerable<string> GetSupportedSitefinityTypes() => existingContentTypeMappings.Keys;

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
}

/// <summary>
/// Information about an existing Kentico content type.
/// </summary>
public class ExistingContentTypeInfo
{
    /// <summary>
    /// The GUID of the existing content type in Kentico database.
    /// </summary>
    public required Guid ClassGUID { get; set; }

    /// <summary>
    /// The class name of the existing content type (e.g., "ContentBase.EventDetail").
    /// </summary>
    public required string ClassName { get; set; }

    /// <summary>
    /// The content type type ("Website" or "Reusable").
    /// </summary>
    public required string ClassContentTypeType { get; set; }
}

/// <summary>
/// Interface for the existing content type mapping service.
/// </summary>
internal interface IExistingContentTypeMappingService
{
    /// <summary>
    /// Gets the existing Kentico content type information for a given Sitefinity type name.
    /// </summary>
    /// <param name="sitefinityTypeName">The Sitefinity content type name</param>
    /// <returns>Existing content type information if found, null otherwise</returns>
    public ExistingContentTypeInfo? GetExistingContentType(string? sitefinityTypeName);

    /// <summary>
    /// Creates a DataClassModel from existing content type information.
    /// </summary>
    /// <param name="existingContentType">Existing content type information</param>
    /// <returns>DataClassModel representing the existing content type</returns>
    public DataClassModel CreateDataClassModelFromExisting(ExistingContentTypeInfo existingContentType);

    /// <summary>
    /// Gets all supported Sitefinity type names that have existing Kentico mappings.
    /// </summary>
    /// <returns>Collection of supported Sitefinity type names</returns>
    public IEnumerable<string> GetSupportedSitefinityTypes();
}
