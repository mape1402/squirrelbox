using Microsoft.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore;

internal interface ISquirrelBoxDbContextFactory<TDbContext>
    where TDbContext : DbContext
{
    TDbContext CreateDbContext();
}
