using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox.EntityFrameworkCore.Internal;

internal sealed class SquirrelBoxDbContextOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo _info;

    public SquirrelBoxDbContextOptionsExtension()
        : this(new SquirrelBoxEntityFrameworkOptions())
    {
    }

    public SquirrelBoxDbContextOptionsExtension(SquirrelBoxEntityFrameworkOptions options)
    {
        Options = options?.Clone() ?? new SquirrelBoxEntityFrameworkOptions();
    }

    public SquirrelBoxEntityFrameworkOptions Options { get; }

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
        private readonly SquirrelBoxDbContextOptionsExtension _extension;

        public ExtensionInfo(SquirrelBoxDbContextOptionsExtension extension)
            : base(extension)
        {
            _extension = extension;
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using SquirrelBox ";

        public override int GetServiceProviderHashCode()
            => HashCode.Combine(
                typeof(SquirrelBoxDbContextOptionsExtension),
                _extension.Options.GetModelHashCode());

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["SquirrelBox"] = _extension.Options.GetModelHashCode().ToString();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo &&
               otherInfo._extension.Options.GetModelHashCode() == _extension.Options.GetModelHashCode();
    }
}
