namespace SquirrelBox.Tests;

public sealed class InboxPolicyOptionsTests
{
    [Theory]
    [InlineData(InboxOpenState.Opened, InboxPolicyAction.Continue)]
    [InlineData(InboxOpenState.Continued, InboxPolicyAction.Continue)]
    [InlineData(InboxOpenState.DuplicateCompleted, InboxPolicyAction.Replay)]
    [InlineData(InboxOpenState.DuplicateInProgress, InboxPolicyAction.Skip)]
    [InlineData(InboxOpenState.DuplicateFailed, InboxPolicyAction.Retry)]
    [InlineData(InboxOpenState.PayloadConflict, InboxPolicyAction.Reject)]
    [InlineData(InboxOpenState.Expired, InboxPolicyAction.Reject)]
    [InlineData(InboxOpenState.MissingIdempotencyKey, InboxPolicyAction.Reject)]
    public void Resolve_returns_configured_action_for_each_known_open_state(
        InboxOpenState state,
        InboxPolicyAction expected)
    {
        var options = new InboxPolicyOptions();

        Assert.Equal(expected, options.Resolve(state));
    }

    [Fact]
    public void Resolve_rejects_unknown_open_state()
    {
        var options = new InboxPolicyOptions();

        Assert.Equal(InboxPolicyAction.Reject, options.Resolve((InboxOpenState)999));
    }
}
