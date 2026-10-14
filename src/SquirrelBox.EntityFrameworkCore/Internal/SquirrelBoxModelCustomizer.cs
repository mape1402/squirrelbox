using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SquirrelBox.EntityFrameworkCore.Internal;

internal sealed class SquirrelBoxModelCustomizer : ModelCustomizer
{
    public SquirrelBoxModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<SquirrelBoxDbContextOptionsExtension>();

        modelBuilder.ApplySquirrelBox(extension?.Options ?? new SquirrelBoxEntityFrameworkOptions());
    }
}
