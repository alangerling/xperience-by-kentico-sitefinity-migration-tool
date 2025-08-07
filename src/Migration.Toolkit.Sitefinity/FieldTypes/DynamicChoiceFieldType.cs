using System.Xml.Linq;

using CMS.Helpers;

using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Abstractions;
using Migration.Toolkit.Sitefinity.Core;
using System.Text.Json;

using Kentico.Xperience.UMT.Model;

using Progress.Sitefinity.RestSdk.Dto;

namespace Migration.Toolkit.Sitefinity.FieldTypes;
/// <summary>
/// Field type for Sitefinity Dynamic Choice field: "Telerik.Sitefinity.Web.UI.Fields.DynamicChoiceField".
/// </summary>
public class DynamicChoiceFieldType : FieldTypeBase, IFieldType
{
    public string SitefinityWidgetTypeName => "Telerik.Sitefinity.Web.UI.Fields.DynamicChoiceField";
    public override string GetColumnType(Field sitefinityField)
    {
        string fieldKey = sitefinityField.FieldName ?? sitefinityField.Name ?? string.Empty;

        if (fieldKey.Equals("issueyear", StringComparison.InvariantCultureIgnoreCase) ||
            fieldKey.Equals("issuemonth", StringComparison.InvariantCultureIgnoreCase))
        {
            return "taxonomy";
        }

        // Default: defer to base implementation if available, else fallback to string
        return base.GetColumnType(sitefinityField) ?? "string";
    }

    public override FormFieldSettings GetSettings(Field sitefinityField)
    {
        string fieldKey = sitefinityField.FieldName ?? sitefinityField.Name ?? string.Empty;

        if (fieldKey.Equals("issuemonth", StringComparison.InvariantCultureIgnoreCase))
        {
            return new FormFieldSettings
            {
                ControlName = "Kentico.Administration.TagSelector",
                CustomProperties = new Dictionary<string, object?>
                {
                    { "MinSelectedTagsCount", sitefinityField.IsRequired ? "1" : "0" },
                    { "TaxonomyGroup", JsonSerializer.Serialize(new[] { "88558805-D283-4F4A-8527-7EB4208B6C93" }) }
                }
            };
        }
        if (fieldKey.Equals("issueyear", StringComparison.InvariantCultureIgnoreCase))
        {
            return new FormFieldSettings
            {
                ControlName = "Kentico.Administration.TagSelector",
                CustomProperties = new Dictionary<string, object?>
                {
                    { "MinSelectedTagsCount", sitefinityField.IsRequired ? "1" : "0" },
                    { "TaxonomyGroup", JsonSerializer.Serialize(new[] { "17D42AEF-D1E1-4A95-8111-6DBA7D65CDE6" }) }
                }
            };
        }

        var options = new List<string>();

        if (sitefinityField.Choices == null)
        {
            return Default(options);
        }

        var xmlDoc = XDocument.Parse(sitefinityField.Choices);
        xmlDoc.Element("choices")?.Descendants("choice").ToList().ForEach(item =>
        {
            string option = "";
            if (item.Attribute("value") != null)
            {
                option += item.Attribute("value")?.Value;
            }

            if (item.Attribute("value") != null && item.Attribute("text") != null)
            {
                option += ";";
            }

            if (item.Attribute("text") != null)
            {
                option += item.Attribute("text")?.Value;
            }

            options.Add(option);
        });

        if (sitefinityField.ChoiceRenderType == null)
        {
            return Default(options);
        }

        if (sitefinityField.ChoiceRenderType.Equals("DropDownList"))
        {
            return new FormFieldSettings
            {
                ControlName = "Kentico.Administration.DropDownSelector",
                CustomProperties = new()
                {
                    { "OptionsValueSeparator", ";" },
                    { "Options", options.Join("\r\n") }
                }
            };
        }

        if (sitefinityField.ChoiceRenderType.Equals("RadioButton"))
        {
            return new FormFieldSettings
            {
                ControlName = "Kentico.Administration.RadioGroup",
                CustomProperties = new()
                {
                    { "Inline", "False" },
                    { "OptionsValueSeparator", ";" },
                    { "Options", options.Join("\r\n") }
                }
            };
        }

        return Default(options);
    }

    private FormFieldSettings Default(IEnumerable<string> options) => new()
    {
        ControlName = "Kentico.Administration.DropDownSelector",
        CustomProperties = new()
        {
            { "OptionsValueSeparator", ";" },
            { "Options", options.Join("\r\n") }
        }
    };
    /*
TaxonomyName	TaxonomyGUID
IssueYear	17D42AEF-D1E1-4A95-8111-6DBA7D65CDE6
IssueMonth	88558805-D283-4F4A-8527-7EB4208B6C93

Value	TagGUID
2018	A540EBA3-E16F-4200-97BB-725DE409F7A5
2019	731E9DAE-26C4-4E64-9CF6-7B43BAFDDDC9
2020	FB949DBF-74F0-4E5F-A839-66A73BBED8DF
2021	549B2F6D-C77E-427A-9BC2-AE98BE4DCCD5
2022	6AB69E07-0552-4859-B64B-2C7046E4336A
2023	0587950C-378D-4916-BF57-9CED86F4F26C
2024	71327F60-5A5C-428C-9F0F-CD5239829F1F
1	3B2E53C8-DAB4-4493-9948-950D98A3B4F0
2	6C7B5812-49FE-4CC7-8BAA-4075F934E6CC
3	53A37042-0993-4855-A9B3-CE9A87A3A07B
4	1171BAA6-66E1-4CC8-94DA-52629683FCC9
5	DD4D508A-5ADC-4E9D-93D9-505BA133AC96
6	D24E85AC-3011-4791-8E9E-3311C36F068E
7	7F2659CC-DE91-41D0-8F24-68CE8B8709BD
     */

    public override object GetData(SdkItem sdkItem, string fieldName)
    {
        // Mapping for IssueYear values to GUIDs
        static Guid? MapIssueYear(string value) => value switch
        {
            "1" => Guid.Parse("A540EBA3-E16F-4200-97BB-725DE409F7A5"),
            "2" => Guid.Parse("731E9DAE-26C4-4E64-9CF6-7B43BAFDDDC9"),
            "4" => Guid.Parse("FB949DBF-74F0-4E5F-A839-66A73BBED8DF"),
            "8" => Guid.Parse("549B2F6D-C77E-427A-9BC2-AE98BE4DCCD5"),
            "16" => Guid.Parse("6AB69E07-0552-4859-B64B-2C7046E4336A"),
            "32" => Guid.Parse("0587950C-378D-4916-BF57-9CED86F4F26C"),
            "64" => Guid.Parse("71327F60-5A5C-428C-9F0F-CD5239829F1F"),
            "128" => Guid.Parse("619F657E-ACA4-40D1-BE15-AC4096C45BFA"),
            "256" => Guid.Parse("46B98A16-C195-4633-9396-D007DDE32572"),
            _ => null
        };

        // Mapping for IssueMonth values to GUIDs
        static Guid? MapIssueMonth(string value) => value switch
        {
            "1" => Guid.Parse("3B2E53C8-DAB4-4493-9948-950D98A3B4F0"),
            "2" => Guid.Parse("6C7B5812-49FE-4CC7-8BAA-4075F934E6CC"),
            "4" => Guid.Parse("53A37042-0993-4855-A9B3-CE9A87A3A07B"),
            "8" => Guid.Parse("1171BAA6-66E1-4CC8-94DA-52629683FCC9"),
            "16" => Guid.Parse("DD4D508A-5ADC-4E9D-93D9-505BA133AC96"),
            "32" => Guid.Parse("D24E85AC-3011-4791-8E9E-3311C36F068E"),
            "64" => Guid.Parse("7F2659CC-DE91-41D0-8F24-68CE8B8709BD"),
            _ => null
        };

        object fieldValue = sdkItem.GetValue<object>(fieldName);

        // Handle IssueYear and IssueMonth mapping
        if (fieldName.Equals("IssueYear", StringComparison.OrdinalIgnoreCase))
        {
            var identifiers = new List<object>();

            if (fieldValue is string strValue && !string.IsNullOrWhiteSpace(strValue))
            {
                var mappedGuid = MapIssueYear(strValue);

                if (mappedGuid.HasValue)
                {
                    identifiers.Add(new { Identifier = mappedGuid.Value });
                }
            }
            else if (fieldValue is IEnumerable<object> enumerable)
            {
                foreach (object? item in enumerable)
                {
                    string? value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var mappedGuid = MapIssueYear(value);

                        if (mappedGuid.HasValue)
                        {
                            identifiers.Add(new { Identifier = mappedGuid.Value });
                        }
                    }
                }
            }

            // Return empty array JSON when no identifiers found
            return JsonSerializer.Serialize(identifiers);
        }

        // Handle IssueMonth mapping
        if (fieldName.Equals("IssueMonth", StringComparison.OrdinalIgnoreCase))
        {
            var identifiers = new List<object>();

            if (fieldValue is string strValue && !string.IsNullOrWhiteSpace(strValue))
            {
                var mappedGuid = MapIssueMonth(strValue);

                if (mappedGuid.HasValue)
                {
                    identifiers.Add(new { Identifier = mappedGuid.Value });
                }
            }
            else if (fieldValue is IEnumerable<object> enumerable)
            {
                foreach (object? item in enumerable)
                {
                    string? value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        var mappedGuid = MapIssueMonth(value);

                        if (mappedGuid.HasValue)
                        {
                            identifiers.Add(new { Identifier = mappedGuid.Value });
                        }
                    }
                }
            }

            // Return empty array JSON when no identifiers found
            return JsonSerializer.Serialize(identifiers);
        }

        // Default: return the raw value
        return fieldValue ?? string.Empty;
    }
}
