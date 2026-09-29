using Microsoft.AspNetCore.Http;

namespace SquirrelBox.AspNetCore;

internal static class SquirrelBoxPayloadFilterExecutor
{
    public static async ValueTask<bool> TryAcceptAsync(
        HttpContext httpContext,
        object payload,
        IInboxService inbox,
        IInboxPolicyResolver policyResolver,
        SquirrelBoxAspNetCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(policyResolver);
        ArgumentNullException.ThrowIfNull(options);

        if (payload is null)
            return true;

        SquirrelBoxHttpInbox.EnsureIdentityHeadersOnStarting(httpContext, inbox, options);

        var open = await inbox.OpenOrContinueAsync(
            SquirrelBoxHttpInbox.CreateOpenRequest(httpContext, options, payload),
            httpContext.RequestAborted);
        var decision = policyResolver.Resolve(open);

        if (decision.Action is not InboxPolicyAction.Continue)
        {
            if (decision.Action is InboxPolicyAction.Replay &&
                await SquirrelBoxHttpInbox.TryReplayDecisionAsync(httpContext, options, decision))
            {
                return false;
            }

            await SquirrelBoxHttpInbox.WriteRejectedDecisionAsync(httpContext, decision);
            return false;
        }

        var verification = await inbox.VerifyCurrentPayloadAsync(payload, httpContext.RequestAborted);
        if (!verification.Success)
        {
            await SquirrelBoxHttpInbox.WritePayloadVerificationConflictAsync(httpContext, verification);
            return false;
        }

        return true;
    }
}
