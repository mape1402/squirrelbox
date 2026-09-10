using Microsoft.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore;

internal sealed class SquirrelBoxDbContextFactoryAdapter<TDbContext> : ISquirrelBoxDbContextFactory<TDbContext>
    where TDbContext : DbContext
{
    private readonly IDbContextFactory<TDbContext> _dbContextFactory;

    public SquirrelBoxDbContextFactoryAdapter(IDbContextFactory<TDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public TDbContext CreateDbContext()
        => _dbContextFactory.CreateDbContext();
}
