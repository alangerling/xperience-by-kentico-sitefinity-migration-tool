using CMS.Helpers;

using Kentico.Xperience.UMT.Model;
using Kentico.Xperience.UMT.Services;

using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Configuration;
using Migration.Toolkit.Sitefinity.Core.Adapters;
using Migration.Toolkit.Sitefinity.Core.Services;
using Migration.Toolkit.Sitefinity.Model;

namespace Migration.Toolkit.Sitefinity.Services;
internal class DataClassImportService(IImportService kenticoImportService,
                                        IChannelImportService channelImportService,
                                        ITypeProvider typeProvider,
                                        SitefinityImportConfiguration configuration,
                                        IUmtAdapterWithDependencies<SitefinityType, DataClassDependencies> adapter) : IDataClassImportService
{
    public IEnumerable<IUmtModel> Get(DataClassDependencies dependenciesModel)
    {
        var dataClassModels = new List<IUmtModel>();

        var types = typeProvider.GetDynamicModuleTypes();
        dataClassModels.AddRange(adapter.Adapt(types, dependenciesModel));

        var staticTypes = typeProvider.GetSitefinityTypes();
        dataClassModels.AddRange(adapter.Adapt(staticTypes, dependenciesModel));
        var mediaTypes = typeProvider.GetMediaContentTypes();
        dataClassModels.AddRange(adapter.Adapt(mediaTypes, dependenciesModel));

        return dataClassModels;
    }

    public SitefinityImportResult StartImport(ImportStateObserver observer)
    {
        var channels = channelImportService.StartImport(observer);

        observer.ImportCompletedTask.Wait();

        var dependencies = new DataClassDependencies
        {
            Channels = channels.ImportedModels.Values.OfType<ChannelModel>().ToDictionary(x => x.ChannelGUID)
        };

        return Import(observer, dependencies);
    }

    public SitefinityImportResult StartImportWithDependencies(ImportStateObserver observer, DataClassDependencies dependenciesModel) => Import(observer, dependenciesModel);

    private SitefinityImportResult Import(ImportStateObserver observer, DataClassDependencies dependencies)
    {
        var dataClasses = Get(dependencies);

        // Filter to include all non-DataClassModel items and exclude DataClassModel items with ClassName starting with "elfa." or "contentbase."
        var filteredDataClasses = dataClasses
            .Where(x => x is not DataClassModel ||
                       (x is DataClassModel dataClassModel &&
                        !string.IsNullOrEmpty(dataClassModel.ClassName) &&
                        !dataClassModel.ClassName.StartsWith("elfa.", StringComparison.OrdinalIgnoreCase) &&
                        !dataClassModel.ClassName.StartsWith("contentbase.", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var importedModels = new Dictionary<Guid, IUmtModel>();

        // Add all dataClasses to importedModels for content item dependencies
        foreach (var dataClass in dataClasses.OfType<DataClassModel>())
        {
            var guid = ValidationHelper.GetGuid(dataClass.ClassGUID, Guid.Empty);

            if (guid.Equals(Guid.Empty))
            {
                continue;
            }

            importedModels.Add(guid, dataClass);
        }

        // For existing Kentico data classes (elfa.* and contentbase.*), we need to create placeholder DataClassModel entries
        // so content items can find them in dependencies, but we don't import them since they already exist
        var existingKenticoClasses = dataClasses.OfType<DataClassModel>()
            .Where(dc => !string.IsNullOrEmpty(dc.ClassName) &&
                        (dc.ClassName.StartsWith("elfa.", StringComparison.OrdinalIgnoreCase) ||
                         dc.ClassName.StartsWith("contentbase.", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var existingClass in existingKenticoClasses)
        {
            var guid = ValidationHelper.GetGuid(existingClass.ClassGUID, Guid.Empty);
            if (!guid.Equals(Guid.Empty) && !importedModels.ContainsKey(guid))
            {
                // Add to dependencies so content items can reference these classes
                importedModels.Add(guid, existingClass);
            }
        }

        // But only import the filtered ones to Kentico to avoid duplicating existing classes
        return new SitefinityImportResult
        {
            ImportedModels = importedModels,
            Observer = kenticoImportService.StartImport(filteredDataClasses, observer)
        };
    }
}
