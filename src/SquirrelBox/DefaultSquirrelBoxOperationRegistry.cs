using Microsoft.Extensions.Options;

namespace SquirrelBox;

/// <summary>
/// Discovers declared operation types from configured assemblies.
/// </summary>
public sealed class DefaultSquirrelBoxOperationRegistry : ISquirrelBoxOperationRegistry
{
    private readonly Lazy<IReadOnlyDictionary<(Type Request, Type Result), Type[]>> _operations;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSquirrelBoxOperationRegistry"/> class.
    /// </summary>
    /// <param name="options">The SquirrelBox options.</param>
    public DefaultSquirrelBoxOperationRegistry(IOptions<SquirrelBoxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _operations = new Lazy<IReadOnlyDictionary<(Type Request, Type Result), Type[]>>(
            () => Discover(options.Value));
    }

    /// <inheritdoc />
    public Type Resolve(Type requestType, Type resultType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(resultType);

        if (!_operations.Value.TryGetValue((requestType, resultType), out var operationTypes) ||
            operationTypes.Length == 0)
        {
            throw new InvalidOperationException(
                $"No SquirrelBox operation was discovered for request '{requestType.FullName}' and result '{resultType.FullName}'.");
        }

        if (operationTypes.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple SquirrelBox operations were discovered for request '{requestType.FullName}' and result '{resultType.FullName}'. Use the explicit operation overload.");
        }

        return operationTypes[0];
    }

    private static IReadOnlyDictionary<(Type Request, Type Result), Type[]> Discover(SquirrelBoxOptions options)
        => options.OperationAssemblies
            .Distinct()
            .SelectMany(assembly => assembly.DefinedTypes)
            .Where(type => type is { IsAbstract: false, IsClass: true } &&
                           typeof(ISquirrelBoxOperation).IsAssignableFrom(type.AsType()))
            .Select(type => type.AsType())
            .GroupBy(type =>
            {
                var contract = ResolveOperationContract(type);
                return (contract.Request, contract.Result);
            })
            .ToDictionary(group => group.Key, group => group.ToArray());

    private static (Type Request, Type Result) ResolveOperationContract(Type operationType)
    {
        var contract = operationType
            .GetInterfaces()
            .Append(operationType)
            .SelectMany(type => type.IsGenericType ? [type] : type.BaseType is null ? [] : EnumerateBaseTypes(type.BaseType))
            .FirstOrDefault(type => type.IsGenericType &&
                                    type.GetGenericTypeDefinition() == typeof(SquirrelBoxOperation<,>));

        if (contract is null)
            throw new InvalidOperationException($"Type '{operationType.FullName}' is not a SquirrelBox operation.");

        var arguments = contract.GetGenericArguments();
        return (arguments[0], arguments[1]);
    }

    private static IEnumerable<Type> EnumerateBaseTypes(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            yield return current;
    }
}
