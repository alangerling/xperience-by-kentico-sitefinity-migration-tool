using System.Text.Json;

using Kentico.Xperience.UMT.Model;

using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Abstractions;
using Migration.Toolkit.Sitefinity.Core;
using Migration.Toolkit.Sitefinity.Model;

using Progress.Sitefinity.RestSdk.Dto;

using Microsoft.Extensions.Logging;

namespace Migration.Toolkit.Sitefinity.FieldTypes;
/// <summary>
/// Field type for Sitefinity RelatedData field: "Telerik.Sitefinity.Web.UI.Fields.RelatedDataField".
/// </summary>
public class RelatedDataFieldType(ITypeProvider typeProvider, ILogger<RelatedDataFieldType> logger) : FieldTypeBase, IFieldType
{
    private IEnumerable<SitefinityType>? sitefinityTypes;

    public string SitefinityWidgetTypeName => "Telerik.Sitefinity.Web.UI.Fields.RelatedDataField";

    public override string GetColumnType(Field sitefinityField)
    {
        if (sitefinityField.RelatedDataType == null)
        {
            return "contentitemreference";
        }

        if (sitefinityField.RelatedDataType.Equals("Telerik.Sitefinity.Pages.Model.PageNode")
            || sitefinityField.RelatedDataType.Equals("Telerik.Sitefinity.DynamicTypes.Model.StateCompendium.CompendiumAuthor")
            || sitefinityField.RelatedDataType.Equals("Telerik.Sitefinity.DynamicTypes.Model.EquipmentLeasingandFinance.MagazineAuthor")
            || sitefinityField.RelatedDataType.Equals("Telerik.Sitefinity.DynamicTypes.Model.StateCompendium.Program")
            || sitefinityField.RelatedDataType.Equals("Telerik.Sitefinity.News.Model.NewsItem")
            )
        {
            return "webpages";
        }

        return "contentitemreference";
    }

    public override FormFieldSettings GetSettings(Field sitefinityField)
    {
        sitefinityTypes ??= typeProvider.GetAllTypes();

        if (sitefinityField.RelatedDataType != null && sitefinityField.RelatedDataType.Equals("Telerik.Sitefinity.Pages.Model.PageNode"))
        {
            return new FormFieldSettings
            {
                ControlName = "Kentico.Administration.WebPageSelector",
                CustomProperties = new Dictionary<string, object?>
                {
                    { "TreePath", "/" },
                }
            };
        }

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

    public override object GetData(SdkItem sdkItem, string fieldName)
    {
        var relatedData = sdkItem.GetValue<IEnumerable<SdkItem>>(fieldName);

        if (relatedData == null)
        {
            // Return an empty JSON array for no related data
            return JsonSerializer.Serialize(new List<ContentRelatedItem>());
        }

        var contentRelatedItems = new List<ContentRelatedItem>();

        foreach (var item in relatedData)
        {
            if (Guid.TryParse(item.Id, out var result))
            {
                contentRelatedItems.Add(new ContentRelatedItem
                {
                    Identifier = result
                });
            }
        }

        if (contentRelatedItems.Count == 0)
        {
            // Return an empty JSON array for no valid related items
            return JsonSerializer.Serialize(new List<ContentRelatedItem>());
        }

        return JsonSerializer.Serialize(contentRelatedItems);
    }
}
