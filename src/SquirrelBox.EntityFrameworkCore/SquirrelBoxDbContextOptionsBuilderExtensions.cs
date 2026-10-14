using Microsoft.EntityFrameworkCore.Infrastructure;
using SquirrelBox.EntityFrameworkCore.Internal;
using SquirrelBox.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore;

internal static class SquirrelBoxDbContextOptionsBuilderExtensions
{
    internal static DbContextOptionsBuilder UseSquirrelBoxModel(
        this DbContextOptionsBuilder optionsBuilder,
        SquirrelBoxEntityFrameworkOptions options)
    {
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new SquirrelBoxDbContextOptionsExtension(options));
        return optionsBuilder;
    }
}
