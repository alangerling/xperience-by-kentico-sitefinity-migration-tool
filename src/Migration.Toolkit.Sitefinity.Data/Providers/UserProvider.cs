using Microsoft.EntityFrameworkCore;

using Migration.Toolkit.Data.Core.EF;
using Migration.Toolkit.Data.Core.Providers;
using Migration.Toolkit.Data.Models;

namespace Migration.Toolkit.Data.Providers;
internal class UserProvider(IDbContextFactory<SitefinityContext> sitefinityContext) : IUserProvider
{
    public IEnumerable<User> GetUsers()
    {
        using var context = sitefinityContext.CreateDbContext();

        // Filter to only include backend users (exclude member/frontend users)
        // Backend users are administrators and content creators, while frontend users are website members
        var users = context.Users
            .Where(u => u.IsBackendUser) // Only include backend users, exclude member submissions
            .ToList();

        return users;
    }
}
