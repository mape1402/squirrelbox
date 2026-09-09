namespace SquirrelBox;

public enum InboxOpenState
{
    Opened,
    Continued,
    DuplicateInProgress,
    DuplicateCompleted,
    DuplicateFailed,
    PayloadConflict,
    Expired,
    MissingIdempotencyKey
}
