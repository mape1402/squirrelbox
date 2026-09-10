using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox.EntityFrameworkCore.Internal;

internal sealed class SquirrelBoxDbContextOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IModelCustomizer, SquirrelBoxModelCustomizer>());
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using SquirrelBox ";

        public override int GetServiceProviderHashCode()
            => typeof(SquirrelBoxDbContextOptionsExtension).GetHashCode();

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["SquirrelBox"] = "1";

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;
    }
}
