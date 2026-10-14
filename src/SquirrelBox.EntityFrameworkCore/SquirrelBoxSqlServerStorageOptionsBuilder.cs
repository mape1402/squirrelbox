namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Builds SQL Server specific SquirrelBox Entity Framework Core storage options.
/// </summary>
public sealed class SquirrelBoxSqlServerStorageOptionsBuilder
{
    private readonly SquirrelBoxEntityFrameworkOptionsBuilder _builder;

    internal SquirrelBoxSqlServerStorageOptionsBuilder(SquirrelBoxEntityFrameworkOptionsBuilder builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <summary>
    /// Stores ULID values as SQL Server <c>binary(16)</c> columns.
    /// </summary>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxSqlServerStorageOptionsBuilder StoreUlidsAsBinary16()
    {
        _builder.StoreUlidsAsBinary16("binary(16)");
        return this;
    }

    /// <summary>
    /// Stores ULID values as SQL Server <c>char(26)</c> columns.
    /// </summary>
    /// <returns>The same builder instance.</returns>
    public SquirrelBoxSqlServerStorageOptionsBuilder StoreUlidsAsText()
    {
        _builder.StoreUlidsAsText("char(26)");
        return this;
    }
}
