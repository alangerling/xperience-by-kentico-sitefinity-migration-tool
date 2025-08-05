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
/// Field type for Sitefinity StateTaxonomy field: "Telerik.Sitefinity.Web.UI.Fields.RelatedDataField".
/// </summary>
public class StateTaxonomyFieldType(ITypeProvider typeProvider, ILogger<StateTaxonomyFieldType> logger) : FieldTypeBase, IFieldType
{
    private static readonly Dictionary<string, string> stateTaxonomy = new()
    {
        {"Alabama", "54271EDC-A115-47FE-9BD6-211BB2FE937B"},
        {"Alaska", "321FB43F-6179-47A8-AAEA-F8439B5200C3"},
        {"Arizona", "7E3D64F2-A3D4-439A-AB65-B33EA7BC58AC"},
        {"Arkansas", "52353ECF-68F0-491A-961C-40AEC13A480A"},
        {"California", "45D460B8-9F61-4A95-AD1C-D44149F6CBE1"},
        {"Colorado", "9CB6BB70-952B-4240-99B4-83627C2C126E"},
        {"Connecticut", "B8C64CCA-6DCB-4D93-8C72-BCCEA45C0FF7"},
        {"Delaware", "A570CAF0-C86C-454A-85C9-500B9256330D"},
        {"DistrictOfColumbia", "973DFC49-E7B5-4B97-B2BD-EBF360E3238D"},
        {"Florida", "36753DBE-59A6-4707-8B12-C5C846AB65E2"},
        {"Georgia", "1B3E5D9A-EE21-4370-AFEA-7540739DBD1B"},
        {"Hawaii", "4E690684-59B3-46FF-9D87-D45839364F71"},
        {"Idaho", "78564756-591C-4665-A03A-2A5AECFFC3CD"},
        {"Illinois", "252C2A70-0F51-462F-B509-1DBB4E5A6683"},
        {"Indiana", "C97DCF71-D130-4AE3-AF38-636A25D2FB7B"},
        {"Iowa", "3DC27622-71AD-4E88-A3B1-DA99BD518A76"},
        {"Kansas", "9E4CA9C9-F8D1-442A-A826-9677016F7BAE"},
        {"Kentucky", "F1BEC5F7-4216-49E6-B6A1-615D3C5A2B63"},
        {"Louisiana", "6F9A4BD7-ACC0-45A8-8C1B-648BF79F5C85"},
        {"Maine", "6ABA93DD-96BA-45CB-BDFA-1D45B9D92C9D"},
        {"Maryland", "F461A443-2A05-4775-ACA2-1FA210CFCE1D"},
        {"Massachusetts", "CEA89132-7757-4F9C-B28F-668541E400AB"},
        {"Michigan", "01B9FBB1-1CFF-4C8C-93AD-4007F8F68CC6"},
        {"Minnesota", "B53676EC-4E65-4F47-BCD6-5E5E6DA7A85D"},
        {"Mississippi", "D08C8820-396F-4A52-9656-4B3984ACB435"},
        {"Missouri", "26DC5A6E-2A90-4174-870B-C0CEE438CDD8"},
        {"Montana", "CE5CE14C-5A9B-4F89-A7E5-9C793C854887"},
        {"Nebraska", "1051FACC-B299-4F93-B342-7BA0B440C76F"},
        {"Nevada", "5EFA3E9A-4CB4-458F-8E33-DC87D9409ED1"},
        {"NewHampshire", "F81ABC29-577C-47C9-AE13-07769339E9B1"},
        {"NewJersey", "181DE7B1-3D03-4D91-BE73-36B9091A5A13"},
        {"NewMexico", "11451B0E-3A07-475A-8AC1-B702ED4648DF"},
        {"NewYork", "2C22A63C-2184-4164-B1B5-CB170A5583DA"},
        {"NorthCarolina", "F7E60C1C-3E19-43A4-AF2C-7CE1CA5CDEA6"},
        {"NorthDakota", "505C2217-96A4-42DF-865E-C50C44A9C0DE"},
        {"Ohio", "4D8311C5-790F-4435-A79B-C6995578E7E7"},
        {"Oklahoma", "979E8D89-7A3E-49D3-87E2-753622E4AABE"},
        {"Ontario_Canada", "5A81A6FF-2A1C-498B-BDF2-033C8067F282"},
        {"Oregon", "B820D1B4-4795-493E-AED8-D76616C734E4"},
        {"Pennsylvania", "320CE441-C5BF-48FA-B465-B31D82BFEC43"},
        {"Quebec_Canada", "80354818-29B3-4C77-A629-1AFAD04A537D"},
        {"RhodeIsland", "7DD7EBA7-8636-433A-820E-E59B53B6BD8E"},
        {"SouthCarolina", "10B88616-097A-42E2-BEAB-3A9F7942F8EC"},
        {"SouthDakota", "AD17679B-EE6C-4DC3-9CB6-F60EF07217F8"},
        {"Tennessee", "05D6D407-45F0-4AEE-8506-B1EF73C1DDE2"},
        {"Texas", "5C72583D-BC79-49A5-8995-C9029F5B5427"},
        {"Utah", "B43E5F8C-1D05-4D38-B067-AF00782A757A"},
        {"Vermont", "9F9B67BD-216C-4335-B770-0A7986914CF5"},
        {"Virginia", "B6A5A599-A16E-482D-9F7C-1B873F6C297E"},
        {"Washington", "01706762-804F-4CDF-88C2-6FE0F3430FBF"},
        {"WestVirginia", "51D9E060-3568-4B7B-8AC7-7AE014ABC60E"},
        {"Wisconsin", "C5718057-7FBE-4AD6-B1B0-8DA7E0E2018A"},
        {"Wyoming", "ECE6D136-E20D-4B55-BB0F-1FC9E2950CDA"}
    };

    private IEnumerable<SitefinityType>? sitefinityTypes;

    public string SitefinityWidgetTypeName => "Telerik.Sitefinity.Web.UI.Fields.StateTaxonomyField";

    public override string GetColumnType(Field sitefinityField) => "taxonomy";

    public override FormFieldSettings GetSettings(Field sitefinityField)
    {
        sitefinityTypes ??= typeProvider.GetAllTypes();

        return new FormFieldSettings
        {
            ControlName = "Kentico.Administration.TagSelector",
            CustomProperties = new Dictionary<string, object?>
            {
                { "MinSelectedTagsCount", "0" },
                { "TaxonomyGroup", JsonSerializer.Serialize(new[] { "953156C1-D73E-49C3-B7DF-1A943BC90B84" }) }
            }
        };
    }

    public override object GetData(SdkItem sdkItem, string fieldName)
    {
        // Inline declaration and assignment of path using pattern matching
        string path = sdkItem is ICultureSdkItem cultureSdkItem ? cultureSdkItem.Url : string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return JsonSerializer.Serialize(new List<ContentRelatedItem>());
        }

        // Extract the first folder from the path
        string[] segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return JsonSerializer.Serialize(new List<ContentRelatedItem>());
        }

        string stateKey = segments[0].ToLowerInvariant().Replace(" ", "");

        // Lowercase the dictionary for case-insensitive lookup
        var stateTaxonomyLower = stateTaxonomy
            .ToDictionary(kvp => kvp.Key.ToLowerInvariant(), kvp => kvp.Value);

        if (stateTaxonomyLower.TryGetValue(stateKey, out string? guidString) && Guid.TryParse(guidString, out var guid))
        {
            List<ContentRelatedItem> result =
            [
                new ContentRelatedItem { Identifier = guid }
            ];
            return JsonSerializer.Serialize(result);
        }

        // If not found, return empty array
        return JsonSerializer.Serialize(new List<ContentRelatedItem>());
    }
}
