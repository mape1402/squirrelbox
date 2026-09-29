using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Text.Json;

namespace SquirrelBox.AspNetCore;

internal static class SquirrelBoxHttpInbox
{
    private static readonly object IdentityHeadersRegistrationKey = new();
    private static readonly object SuppressCompletionKey = new();

    public static bool HasPayloadMetadata(HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ISquirrelBoxPayloadMetadata>() is not null)
            return true;

        var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return action is not null &&
               (action.EndpointMetadata.OfType<ISquirrelBoxPayloadMetadata>().Any() ||
                action.FilterDescriptors.Any(descriptor => descriptor.Filter is ISquirrelBoxPayloadMetadata));
    }

    public static void EnsureIdentityHeadersOnStarting(
        HttpContext httpContext,
        IInboxService inbox,
        SquirrelBoxAspNetCoreOptions options)
    {
        if (httpContext.Items.ContainsKey(IdentityHeadersRegistrationKey))
            return;

        httpContext.Items[IdentityHeadersRegistrationKey] = true;
        httpContext.Response.OnStarting(() =>
        {
            var identity = inbox.LastContext?.Identity ?? inbox.Current?.Identity;
            if (identity is not null)
                AttachIdentityHeaders(httpContext, options, identity);

            return Task.CompletedTask;
        });
    }

    public static void SuppressCompletion(HttpContext httpContext)
        => httpContext.Items[SuppressCompletionKey] = true;

    public static InboxContext ResolveCompletableContext(
        HttpContext httpContext,
        IInboxService inbox,
        InboxOpenResult openResult)
    {
        if (httpContext.Items.ContainsKey(SuppressCompletionKey))
            return null;

        var context = openResult?.Context ?? inbox.Current;
        return context is { OwnsCompletion: true } && ReferenceEquals(inbox.Current, context)
            ? context
            : null;
    }

    public static InboxOpenRequest CreateOpenRequest(
        HttpContext httpContext,
        SquirrelBoxAspNetCoreOptions options,
        object payload = null,
        string operation = null,
        string source = null,
        string owner = null,
        InboxExecutionMode? executionMode = null)
    {
        var idempotencyKey = ResolveRequestHeader(httpContext, options.RequestHeaderNames);
        var correlationId = ResolveRequestHeader(httpContext, options.CorrelationIdHeaderNames);
        var traceId = ResolveRequestHeader(httpContext, options.TraceIdHeaderNames);

        return new InboxOpenRequest
        {
            Source = source ?? options.Source,
            Operation = operation ?? options.OperationResolver(httpContext),
            IdempotencyKey = idempotencyKey.Value,
            IdempotencyKeyName = idempotencyKey.Name,
            Payload = payload,
            PayloadType = payload?.GetType().AssemblyQualifiedName,
            CorrelationId = correlationId.Value,
            CorrelationIdName = correlationId.Name,
            TraceId = string.IsNullOrWhiteSpace(traceId.Value) ? httpContext.TraceIdentifier : traceId.Value,
            TraceIdName = traceId.Name,
            AttemptIdName = ResolveDefaultName(options.AttemptIdHeaderNames, SquirrelBoxMetadataNames.AttemptId),
            Owner = owner ?? options.Owner,
            ExecutionMode = executionMode ?? options.ExecutionModeResolver(httpContext)
        };
    }

    public static ResolvedHeader ResolveRequestHeader(HttpContext httpContext, IEnumerable<string> names)
    {
        foreach (var name in names.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var value = httpContext.Request.Headers[name].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return new ResolvedHeader(name, value);
        }

        return new ResolvedHeader(ResolveDefaultName(names, null), null);
    }

    public static async Task<bool> TryReplayDecisionAsync(
        HttpContext httpContext,
        SquirrelBoxAspNetCoreOptions options,
        InboxDecision decision)
    {
        if (!options.ReplayCompletedResponses)
            return false;

        var completion = decision.Entry?.Completion;
        if (completion?.StatusCode is null)
            return false;

        httpContext.Response.StatusCode = completion.StatusCode.Value;

        if (!string.IsNullOrWhiteSpace(completion.ContentType))
            httpContext.Response.ContentType = completion.ContentType;

        if (decision.Entry?.ToIdentity() is { } identity)
            AttachIdentityHeaders(httpContext, options, identity);

        foreach (var header in completion.Headers)
        {
            if (ShouldReplayHeader(options, header.Key))
                httpContext.Response.Headers[header.Key] = header.Value;
        }

        if (completion.ResultPayload is { Length: > 0 } payload)
            await httpContext.Response.Body.WriteAsync(payload, httpContext.RequestAborted);

        return true;
    }

    public static Task WriteRejectedDecisionAsync(HttpContext httpContext, InboxDecision decision)
    {
        httpContext.Response.StatusCode = decision.Action switch
        {
            InboxPolicyAction.Skip => StatusCodes.Status409Conflict,
            InboxPolicyAction.Replay => StatusCodes.Status409Conflict,
            InboxPolicyAction.Retry => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status409Conflict
        };

        httpContext.Response.ContentType = "application/json";
        return httpContext.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            decision.State,
            decision.Action,
            decision.EffectiveIdempotencyKey,
            decision.Entry?.CorrelationId,
            AttemptId = decision.Entry?.CurrentAttempt?.AttemptId ?? decision.Entry?.LastAttemptId,
            TraceId = decision.Entry?.CurrentAttempt?.TraceId ?? decision.Entry?.LastTraceId
        }));
    }

    public static Task WritePayloadVerificationConflictAsync(
        HttpContext httpContext,
        InboxPayloadVerificationResult result)
    {
        SuppressCompletion(httpContext);
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        httpContext.Response.ContentType = "application/json";

        return httpContext.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            result.State,
            InboxEntryId = result.Entry?.Id.ToString(),
            IdempotencyKey = result.Entry?.IdempotencyKey,
            result.Entry?.CorrelationId,
            AttemptId = result.Entry?.CurrentAttempt?.AttemptId ?? result.Entry?.LastAttemptId,
            TraceId = result.Entry?.CurrentAttempt?.TraceId ?? result.Entry?.LastTraceId
        }));
    }

    public static InboxCompletion CreateCompletion(
        HttpContext httpContext,
        SquirrelBoxAspNetCoreOptions options,
        MemoryStream capturedBody)
    {
        var completion = new InboxCompletion
        {
            ContentType = httpContext.Response.ContentType,
            StatusCode = httpContext.Response.StatusCode,
            ResultPayload = capturedBody.Length <= options.MaxReplayBodyBytes
                ? capturedBody.ToArray()
                : null
        };

        foreach (var headerName in options.CapturedResponseHeaderNames)
        {
            if (httpContext.Response.Headers.TryGetValue(headerName, out var value) &&
                !string.IsNullOrWhiteSpace(value.ToString()))
            {
                completion.Headers[headerName] = value.ToString();
            }
        }

        if (capturedBody.Length > options.MaxReplayBodyBytes)
            completion.Metadata["squirrelbox:http:body-too-large"] = "true";

        return completion;
    }

    public static void AttachIdentityHeaders(
        HttpContext httpContext,
        SquirrelBoxAspNetCoreOptions options,
        SquirrelBoxIdentity identity)
    {
        AttachHeader(httpContext, ResolveIdempotencyHeaderName(options, identity), identity.Operation.IdempotencyKey?.Value);
        AttachHeader(httpContext, identity.Operation.CorrelationId?.Name, identity.Operation.CorrelationId?.Value);
        AttachHeader(httpContext, identity.Attempt.AttemptId?.Name, identity.Attempt.AttemptId?.Value);
        AttachHeader(httpContext, identity.Attempt.TraceId?.Name, identity.Attempt.TraceId?.Value);
    }

    private static bool ShouldReplayHeader(SquirrelBoxAspNetCoreOptions options, string headerName)
        => !string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase) &&
           options.CapturedResponseHeaderNames.Contains(headerName);

    private static string ResolveIdempotencyHeaderName(
        SquirrelBoxAspNetCoreOptions options,
        SquirrelBoxIdentity identity)
        => !string.IsNullOrWhiteSpace(identity.Operation.IdempotencyKey?.Name)
            ? identity.Operation.IdempotencyKey.Name
            : !string.IsNullOrWhiteSpace(options.ResponseHeaderName)
                ? options.ResponseHeaderName
                : ResolveDefaultName(options.RequestHeaderNames, SquirrelBoxMetadataNames.IdempotencyKey);

    private static void AttachHeader(HttpContext httpContext, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            httpContext.Response.Headers[name] = value;
    }

    private static string ResolveDefaultName(IEnumerable<string> names, string fallback)
        => names?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? fallback;

    public sealed record ResolvedHeader(string Name, string Value);
}
