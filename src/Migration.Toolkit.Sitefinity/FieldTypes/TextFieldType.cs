using System.Text.Json;

using Kentico.Xperience.UMT.Model;

using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Abstractions;
using Migration.Toolkit.Sitefinity.Core;

using Progress.Sitefinity.RestSdk.Dto;

namespace Migration.Toolkit.Sitefinity.FieldTypes;
/// <summary>
/// Field type for Sitefinity Text field: "Telerik.Sitefinity.Web.UI.Fields.TextField".
/// </summary>
public class TextFieldType : FieldTypeBase, IFieldType
{
    // Replace with actual taxonomy group GUID for Tax Manual categories
    private readonly Guid articleColumnsTaxonomyGroupGuid = Guid.Parse("F3A2D95C-7836-6C70-9642-FF00005F0421");
    private readonly Guid articleDepartmentsTaxonomyGroupGuid = Guid.Parse("F2A2D95C-7836-6C70-9642-FF00005F0421");
    private readonly Guid articleTypesTaxonomyGroupGuid = Guid.Parse("F1A2D95C-7836-6C70-9642-FF00005F0421");
    private readonly Guid categoriesTaxonomyGroupGuid = Guid.Parse("E5CD6D69-1543-427B-AD62-688A99F5E7D4");
    private readonly Guid creditCriteriaTaxonomyGroupGuid = Guid.Parse("862B4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid cvCompanyTypeTaxonomyGroupGuid = Guid.Parse("9C2B4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid departmentsTaxonomyGroupGuid = Guid.Parse("D7831091-E7B1-41B8-9E75-DFF32D6A7837");
    private readonly Guid elfaBusinessCouncilsTaxonomyGroupGuid = Guid.Parse("A62B4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid equipmentTypesTaxonomyGroupGuid = Guid.Parse("B22B4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid fundingProgramsTaxonomyGroupGuid = Guid.Parse("F92B4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid fundingSourceTypesTaxonomyGroupGuid = Guid.Parse("3E2C4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid fundingSourceCompanyTypesTaxonomyGroupGuid = Guid.Parse("432C4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid leaseStructuresTaxonomyGroupGuid = Guid.Parse("492C4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid lenderTypesTaxonomyGroupGuid = Guid.Parse("712C4D5D-7836-6C70-9642-FF00005F0421");
    private readonly Guid newsTypesTaxonomyGroupGuid = Guid.Parse("C26AB35C-7836-6C70-9642-FF00005F0421");
    private readonly Guid pageTemplatesTaxonomyGroupGuid = Guid.Parse("C09A8A55-E07F-448E-8412-3458770A6652");
    private readonly Guid sponsorshipCategoriesTaxonomyGroupGuid = Guid.Parse("3EA6CE5C-7836-6C70-9642-FF00005F0421");
    private readonly Guid tagsTaxonomyGroupGuid = Guid.Parse("CB0F3A19-A211-48A7-88EC-77495C0F5374");
    private readonly Guid taxManualCategoriesTaxonomyGroupGuid = Guid.Parse("3249025D-7836-6C70-9642-FF00005F0421");

    public string SitefinityWidgetTypeName => "Telerik.Sitefinity.Web.UI.Fields.TextField";

    public override string? GetColumnSize(Field sitefinityField)
    {
        if (sitefinityField.FieldTypeDisplayName == null)
        {
            return sitefinityField.DBLength;
        }

        if (sitefinityField.FieldTypeDisplayName.Equals("Number"))
        {
            return "20";
        }

        return sitefinityField.DBLength;
    }

    public override string GetColumnType(Field sitefinityField)
    {
        if (sitefinityField.FieldTypeDisplayName?.Equals("LongText") == true)
        {
            return "longtext";
        }

        if (sitefinityField.FieldTypeDisplayName?.Equals("Classification") == true || sitefinityField.FieldName?.Equals("Category") == true || sitefinityField.FieldName?.Equals("Tags") == true || sitefinityField.FieldName?.Equals("newstypes") == true)
        {
            return "taxonomy";
        }

        if (sitefinityField.FieldTypeDisplayName?.Equals("Number") == true)
        {
            return "decimal";
        }

        return "text";
    }

    public override FormFieldSettings GetSettings(Field sitefinityField)
    {
        // Check if this is a category field in Tax Manual items that should be converted to taxonomy
        var taxonomyGroupGuid = Guid.Empty;

        // Use FieldName if available, otherwise fallback to Name; make switch case-insensitive
        string? fieldKey = sitefinityField.FieldName ?? sitefinityField.Name;
        taxonomyGroupGuid = (fieldKey?.ToLowerInvariant()) switch
        {
            "articlecolumns" => articleColumnsTaxonomyGroupGuid,
            "articledepartments" => articleDepartmentsTaxonomyGroupGuid,
            "articletypes" => articleTypesTaxonomyGroupGuid,
            "category" or "categories" => categoriesTaxonomyGroupGuid,
            "creditcriteria" => creditCriteriaTaxonomyGroupGuid,
            "cvcompanytype" => cvCompanyTypeTaxonomyGroupGuid,
            "departments" => departmentsTaxonomyGroupGuid,
            "elfabusinesscouncils" => elfaBusinessCouncilsTaxonomyGroupGuid,
            "equipmenttypes" => equipmentTypesTaxonomyGroupGuid,
            "fundingprogram" => fundingProgramsTaxonomyGroupGuid,
            "fundingsourcetypes" => fundingSourceTypesTaxonomyGroupGuid,
            "fundingsourcecompanytypes" => fundingSourceCompanyTypesTaxonomyGroupGuid,
            "leasestructure" => leaseStructuresTaxonomyGroupGuid,
            "lendertype" => lenderTypesTaxonomyGroupGuid,
            "news-types" or "newstypes" => newsTypesTaxonomyGroupGuid,
            "pagetemplates" => pageTemplatesTaxonomyGroupGuid,
            "sponsorshipcategories" or "sponsorship-categories" => sponsorshipCategoriesTaxonomyGroupGuid,
            "tags" => tagsTaxonomyGroupGuid,
            "taxmanualcategories" or "tax-manual-categories" => taxManualCategoriesTaxonomyGroupGuid,
            _ => Guid.Empty,
        };
        if (taxonomyGroupGuid != Guid.Empty)
        {
            return new FormFieldSettings
            {
                ControlName = "Kentico.Administration.TagSelector",
                CustomProperties = new Dictionary<string, object?>
                {
                    { "MinSelectedTagsCount", sitefinityField.IsRequired ? "1" : "0" },
                    { "TaxonomyGroup", JsonSerializer.Serialize(new[] { taxonomyGroupGuid }) }
                }
            };
        }

        if (sitefinityField.FieldTypeDisplayName?.Equals("LongText") == true)
        {
            return new FormFieldSettings
            {
                ControlName = "Kentico.Administration.TextArea"
            };
        }

        if (sitefinityField.FieldTypeDisplayName?.Equals("Number") == true)
        {
            return new FormFieldSettings
            {
                ControlName = "Kentico.Administration.DecimalNumberInput"
            };
        }

        return new FormFieldSettings
        {
            ControlName = "Kentico.Administration.TextInput"
        };
    }

    public override FormField HandleSpecialCase(FormField formField, Field sitefinityField)
    {
        if (sitefinityField.FieldTypeDisplayName == null)
        {
            return formField;
        }

        if (sitefinityField.FieldTypeDisplayName.Equals("Number"))
        {
            formField.Precision = sitefinityField.DecimalPlacesCount ?? 0;
        }

        return formField;
    }

    public override object GetData(SdkItem sdkItem, string fieldName)
    {
        // Get the raw field value from the SDK item
        object fieldValue = sdkItem.GetValue<object>(fieldName);

        // Use FieldName if available, otherwise fallback to Name; make switch case-insensitive
        string? fieldKey = fieldName;

        // List of taxonomy field keys to check
        string[] taxonomyFieldKeys = new[]
        {
            "articlecolumns",
            "articledepartments",
            "articletypes",
            "category",
            "categories",
            "creditcriteria",
            "cvcompanytype",
            "departments",
            "elfabusinesscouncils",
            "equipmenttypes",
            "fundingprogram",
            "fundingsourcetypes",
            "fundingsourcecompanytypes",
            "leasestructure",
            "lendertype",
            "news-types",
            "newstypes",
            "pagetemplates",
            "sponsorshipcategories",
            "sponsorship-categories",
            "tags",
            "taxmanualcategories",
            "tax-manual-categories"
        };

        bool isTaxonomyField = fieldValue != null && fieldKey != null &&
            taxonomyFieldKeys.Any(key => string.Equals(fieldKey, key, StringComparison.OrdinalIgnoreCase));

        if (isTaxonomyField)
        {
            try
            {
                // Handle both Newtonsoft.Json.Linq.JArray and string JSON array
                string[]? guidStrings = null;

                if (fieldValue is string jsonString)
                {
                    guidStrings = JsonSerializer.Deserialize<string[]>(jsonString);
                }
                else if (fieldValue is Newtonsoft.Json.Linq.JArray jArray)
                {
                    guidStrings = jArray.ToObject<string[]>();
                }
                else if (fieldValue is IEnumerable<object> enumerable)
                {
                    guidStrings = enumerable.Select(x => x?.ToString()).Where(x => x != null).ToArray()!;
                }

                if (guidStrings != null)
                {
                    // Use List<object> and anonymous type for serialization compatibility
                    var taxonomies = guidStrings
                        .Where(guidString => Guid.TryParse(guidString, out _))
                        .Select(guidString => (object)new { Identifier = Guid.Parse(guidString) })
                        .ToList();

                    return JsonSerializer.Serialize(taxonomies);
                }
            }
            catch
            {
                // If deserialization fails, return the original value
                return fieldValue ?? string.Empty;
            }
        }

        return fieldValue ?? string.Empty;
    }
}
