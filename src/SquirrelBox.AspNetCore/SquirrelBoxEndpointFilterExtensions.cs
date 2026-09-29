using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// Provides Minimal API endpoint filter registration for SquirrelBox payload-based idempotency.
/// </summary>
public static class SquirrelBoxEndpointFilterExtensions
{
    /// <summary>
    /// Opens or continues a SquirrelBox inbox context from a bound Minimal API endpoint argument.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="argumentName">The endpoint argument name that contains the payload.</param>
    /// <returns>The same route handler builder for fluent configuration.</returns>
    public static RouteHandlerBuilder WithSquirrelBoxPayload(
        this RouteHandlerBuilder builder,
        string argumentName = "request")
    {
        ArgumentNullException.ThrowIfNull(builder);
        var resolvedArgumentName = string.IsNullOrWhiteSpace(argumentName) ? "request" : argumentName;

        builder.WithMetadata(new SquirrelBoxPayloadEndpointMetadata(resolvedArgumentName));
        builder.AddEndpointFilterFactory((factoryContext, next) =>
        {
            var payloadIndex = ResolvePayloadIndex(factoryContext, resolvedArgumentName);

            return async invocationContext =>
            {
                if (payloadIndex < 0 ||
                    payloadIndex >= invocationContext.Arguments.Count ||
                    invocationContext.Arguments[payloadIndex] is not { } payload)
                {
                    return await next(invocationContext);
                }

                var services = invocationContext.HttpContext.RequestServices;
                var accepted = await SquirrelBoxPayloadFilterExecutor.TryAcceptAsync(
                    invocationContext.HttpContext,
                    payload,
                    services.GetRequiredService<IInboxService>(),
                    services.GetRequiredService<IInboxPolicyResolver>(),
                    services.GetRequiredService<IOptions<SquirrelBoxAspNetCoreOptions>>().Value);

                return accepted
                    ? await next(invocationContext)
                    : Results.Empty;
            };
        });

        return builder;
    }

    private static int ResolvePayloadIndex(
        EndpointFilterFactoryContext context,
        string argumentName)
    {
        var parameters = context.MethodInfo.GetParameters();
        for (var index = 0; index < parameters.Length; index++)
        {
            if (string.Equals(parameters[index].Name, argumentName, StringComparison.Ordinal))
                return index;
        }

        return -1;
    }
}
