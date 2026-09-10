using Microsoft.EntityFrameworkCore.Infrastructure;
using SquirrelBox.EntityFrameworkCore.Internal;

namespace Microsoft.EntityFrameworkCore;

internal static class SquirrelBoxDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseSquirrelBoxModel(this DbContextOptionsBuilder optionsBuilder)
    {
        var extension = optionsBuilder.Options.FindExtension<SquirrelBoxDbContextOptionsExtension>()
            ?? new SquirrelBoxDbContextOptionsExtension();

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return optionsBuilder;
    }
}
