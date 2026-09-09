namespace SquirrelBox;

public enum InboxBeginState
{
    Started,
    DuplicateInProgress,
    DuplicateCompleted,
    DuplicateFailed,
    PayloadConflict,
    Expired
}
