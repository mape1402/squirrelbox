using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// Opens or continues a SquirrelBox inbox context from a bound MVC action payload.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SquirrelBoxPayloadAttribute : Attribute, IAsyncActionFilter, ISquirrelBoxPayloadMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPayloadAttribute"/> class.
    /// </summary>
    /// <param name="argumentName">The action argument name that contains the payload.</param>
    public SquirrelBoxPayloadAttribute(string argumentName = "request")
    {
        ArgumentName = string.IsNullOrWhiteSpace(argumentName) ? "request" : argumentName;
    }

    /// <inheritdoc />
    public string ArgumentName { get; }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!context.ActionArguments.TryGetValue(ArgumentName, out var payload) ||
            payload is null)
        {
            await next();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var accepted = await SquirrelBoxPayloadFilterExecutor.TryAcceptAsync(
            context.HttpContext,
            payload,
            services.GetRequiredService<IInboxService>(),
            services.GetRequiredService<IInboxPolicyResolver>(),
            services.GetRequiredService<IOptions<SquirrelBoxAspNetCoreOptions>>().Value);

        if (!accepted)
        {
            context.Result = new EmptyResult();
            return;
        }

        await next();
    }
}
