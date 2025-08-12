using Kentico.Xperience.UMT.Model;
using Kentico.Xperience.UMT.Services;

using Microsoft.Extensions.Logging;

using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;
using Migration.Toolkit.Sitefinity.Core.Adapters;
using Migration.Toolkit.Sitefinity.Core.Services;
using Migration.Toolkit.Sitefinity.Model;

namespace Migration.Toolkit.Sitefinity.Services;
internal class UserImportService(IImportService kenticoImportService,
                                    IUserProvider userProvider,
                                    IUmtAdapter<User, UserInfoModel> mapper,
                                    ILogger<UserImportService> logger) : IUserImportService
{
    public IEnumerable<UserInfoModel> Get()
    {
        var allUsers = userProvider.GetUsers().ToList();

        // Log information about user filtering
        int totalUsers = allUsers.Count;
        logger.LogInformation("Retrieved {TotalUsers} backend users (member/frontend users are excluded by UserProvider)", totalUsers);

        // Group users by email address to detect duplicates
        var emailGroups = allUsers.GroupBy(u => u.Email?.ToLowerInvariant());

        // Clear email field for users with duplicate email addresses
        foreach (var group in emailGroups.Where(g => g.Count() > 1))
        {
            foreach (var user in group.Skip(1))
            {
                logger.LogInformation("Clearing duplicate email for user '{UserName}' (ID: {UserId}) - duplicate email: {Email}",
                    user.UserName, user.Id, user.Email);
                user.Email = null; // Clear email to force fallback email generation
            }
        }

        return mapper.Adapt(allUsers);
    }

    public SitefinityImportResult<UserInfoModel> StartImport(ImportStateObserver observer)
    {
        var importedModels = Get();

        return new SitefinityImportResult<UserInfoModel>
        {
            ImportedModels = importedModels.ToDictionary(x => x.UserGUID),
            Observer = kenticoImportService.StartImport(importedModels, observer)
        };
    }
}
