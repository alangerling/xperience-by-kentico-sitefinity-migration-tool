using System.Text.Json;

using Kentico.Xperience.UMT.Model;

using Microsoft.Extensions.Logging;

using Migration.Toolkit.Data.Configuration;
using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Abstractions;
using Migration.Toolkit.Sitefinity.Core;
using Migration.Toolkit.Sitefinity.Model;

using Progress.Sitefinity.RestSdk.Dto;

namespace Migration.Toolkit.Sitefinity.FieldTypes;
/// <summary>
/// Field type for Sitefinity RelatedData field: "Telerik.Sitefinity.Web.UI.Fields.RelatedDataField".
/// </summary>
public class RelatedProgramsFieldType(ITypeProvider typeProvider, IContentProvider contentProvider, SitefinityDataConfiguration dataConfiguration, ISiteProvider siteProvider, ILogger<RelatedDataFieldType> logger) : FieldTypeBase, IFieldType
{
    private IEnumerable<SitefinityType>? sitefinityTypes;

    public string SitefinityWidgetTypeName => "Telerik.Sitefinity.Web.UI.Fields.RelatedProgramsField";

    public override string GetColumnType(Field sitefinityField) => "contentitemreference";

    public override FormFieldSettings GetSettings(Field sitefinityField)
    {
        sitefinityTypes ??= typeProvider.GetAllTypes();

        var allowedType = sitefinityTypes.FirstOrDefault(x => $"{x.ClassNamespace}.{x.Name}".Equals(sitefinityField.RelatedDataType));

        return new FormFieldSettings
        {
            ControlName = "Kentico.Administration.ContentItemSelector",
            CustomProperties = new Dictionary<string, object?>
            {
                { "SelectionType", "contentTypes" },
                { "AllowedContentItemTypeIdentifiers", $"[\"{allowedType?.Id}\"]" }
            }
        };
    }
    public IEnumerable<ContentItem>? ProgramsContentItems = null;
    public Site? GetCurrentSite()
    {
        var sites = siteProvider.GetSites();
        return sites.FirstOrDefault(x => (x.LiveUrl != null && x.LiveUrl.Equals(dataConfiguration.SitefinitySiteDomain)) || (x.StagingUrl != null && x.StagingUrl.Equals(dataConfiguration.SitefinitySiteDomain)));
    }

    public override object GetData(SdkItem sdkItem, string fieldName)
    {
        if (ProgramsContentItems == null)
        {
            // Retrieve dynamic module types and assign to a variable (explicit type for clarity and CAxxxx compliance)
            var dynamicModuleTypes = typeProvider.GetDynamicModuleTypes();
            var currentSite = GetCurrentSite();

            if (dynamicModuleTypes != null && currentSite != null)
            {
                ProgramsContentItems = contentProvider.GetProgramsContentItems(dynamicModuleTypes.Select(type => new SitefinityTypeDefinition
                {
                    SitefinityTypeNameSpace = type.ClassNamespace ?? string.Empty,
                    SitefinityTypeName = type.Name ?? string.Empty,
                    DataClassGuid = type.Id,
                }), currentSite.SystemCultures);
            }
        }

        var eventId = Guid.Parse(sdkItem.Id);

        if (ProgramsContentItems != null)
        {
            var eventPrograms = ProgramsContentItems.Where(item => Guid.Parse(item.ParentId) == eventId).ToList(); // Force evaluation of the query

            var contentRelatedItems = new List<ContentRelatedItem>();
            foreach (var item in eventPrograms)
            {
                contentRelatedItems.Add(new ContentRelatedItem
                {
                    Identifier = item.Id
                });
            }

            return JsonSerializer.Serialize(contentRelatedItems);
        }
        else
        {
            return JsonSerializer.Serialize(new List<ContentRelatedItem>());
        }

    }
}
